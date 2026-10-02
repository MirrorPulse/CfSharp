# CF-002: mutation USNs and conditional synchronization

## Status

The missing public file-system USN query is addressed by `CloudItem.ReadUsnAsync`.
CF-002 conditional confirmation remains unresolved on the investigated Windows builds. A positive
file-system USN is observable, but the native Cloud Files platform can reject it even when content
has not changed. Do not merge this investigation as a completed conditional-confirmation fix or
close MirrorPulse P017/P018 without the required end-to-end evidence.

## Reported evidence

MirrorPulse reported CfSharp/CfSharp.Storage.Sqlite `0.1.0-preview.2`, source
`32e292f921b0ac33bb0b07dc671616ef3ae990fe`. Its independent public-API fixture returned zero
for conversion, clear, mark, changed clear, and metadata patch on Windows Server build
`10.0.26100.0` with an active USN journal. These successful public results do not contain a native
success HRESULT; none should be inferred from the absence of an exception.

The [native comparison run](https://github.com/mirrorpulse/MirrorPulse/actions/runs/36864359704)
records direct `CfSharp.Native.CfApi.CfSetInSyncState` with `S_OK` (`0x00000000`) and USN zero,
while `fsutil usn readdata` returned `0x00000000d376cb40` for the owned fixture. Those numeric
results were independently checked in the completed job log. They establish neither a missing
journal nor a managed wrapper dropping a nonzero SetInSync result. They do not establish the
native Convert HRESULT on that runner.

## Library and platform contracts

The Convert, Update, and SetInSync implementations pass an eight-byte `long*` directly to Windows
and return that value unchanged. No truncation, nullable conversion, or overwritten native output
was found in the source. Mutation results continue to preserve the native value, including zero.
A later observation must not be substituted for the mutation USN: it could describe an intervening
change that the provider has not verified.

Microsoft documents a final output USN for
[`CfConvertToPlaceholder`](https://learn.microsoft.com/windows/win32/api/cfapi/nf-cfapi-cfconverttoplaceholder).
For [`CfSetInSyncState`](https://learn.microsoft.com/windows/win32/api/cfapi/nf-cfapi-cfsetinsyncstate),
a nonzero input is the USN condition, and input zero is equivalent to a null pointer. The positive
value guards on `CloudInSyncChangeOptions` and placeholder patches remain intact. No unconditional
fallback, predicted USN, retry with a newer unverified token, or acceptance skip is introduced.

## Added query and impact

`CloudItem.ReadUsnAsync` uses
[`FSCTL_READ_FILE_USN_DATA`](https://learn.microsoft.com/windows/win32/api/winioctl/ni-winioctl-fsctl_read_file_usn_data)
to read the last file-system USN on an attribute-only handle. It applies to ordinary files and
directories as well as placeholders, including online-only files without hydration. It uses the
facade's lifecycle and path coordination and retains no handle, cached USN, or durable state.
Native failures preserve the Win32 code as a `CloudFilesException` HRESULT. A successful zero value
is returned honestly and remains unsuitable for conditional synchronization.

The unsafe query and versioned USN-record parser reside in `CfSharp.Native`. The parser accepts
v2 and v3, validates the returned record length, and accounts for the USN field moving from byte
24 to byte 40 with 128-bit ReFS identifiers. SDK ABI probes verify the control code, request layout,
and offsets on x64 and ARM64. ReFS parsing is covered; a live ReFS-volume scenario has not been run.

This is an additive API change. Existing mutation semantics, registration policies, SQLite schema,
and provider callbacks are unchanged. The query adds a synchronous metadata I/O operation and
requires a supported file system and sufficient attribute access. It does not create or enable a
volume journal. Cancellation is observed before the native query; it cannot interrupt that call.

A token does not bind a content hash or a durable item identity. Providers must obtain it before
hash verification, close verification streams, then pass the same positive token to the native
conditional mark. Deletion/replacement and journal recreation require separate reconciliation.
See the [public API example](placeholders.md#conditional-in-sync-confirmation).

## Required acceptance and local investigation

`CloudItemUsnTests` runs independent owned fixtures under both `CloudInSyncPolicy.TrackAll` and
`CloudInSyncPolicy.None`. Read coverage checks ordinary and placeholder files/directories,
content-change observations, online-only non-hydration, cancellation, kind validation, missing-item
native errors, and disposal. Stale-token rejection is checked separately from fresh-token success.

The required `CurrentUsnMustSupportVerifiedConditionalInSync` test compares public mutation outputs
with direct native calls, logs the actual native Convert/SetInSync HRESULTs, compares data and
attribute handles, and reads a USN then calls SetInSync on the same handle. Finally it reads a
positive public token, verifies SHA-256, closes the stream, asserts the USN stayed unchanged, and
requires the public conditional mark to succeed. It is included in normal CI; it is not skipped,
excluded, or turned into an expected rejection.

On local Windows `10.0.26300.0`, native ARM64, public Convert and clear returned zero while the
query returned positive USNs. Direct native Convert and clear each returned `S_OK`/zero on
their owned fixtures. These are captured native results for this local build, not inferred
HRESULTs for the reported Server runner. Positive conditional
SetInSync calls failed with `0x80070179` (`ERROR_CLOUD_FILE_NOT_IN_SYNC`) on standard data handles,
metadata handles, and immediately after reading on the same handle. Hash verification did not
change the observed USN. Both policies reproduced the rejection. This establishes a platform
boundary on this build; it does not establish that every Windows version behaves identically.

The fresh-token acceptance must pass on the target Windows version before claiming that the query
enables MirrorPulse confirmation. Keep the PR in draft while this native acceptance fails. After
that boundary is resolved, rerun MirrorPulse's same `NativeFullRescan` fixture, then collect all
three same-SHA job results with `-RequireNative`/`-RequireInstalled` before closing P017/P018.

Local validation of this change: Release build (zero warnings/errors), formatting, API baseline,
platform matrix, x64/ARM64 SDK ABI comparisons, and trim/Native AOT boundaries all passed.
The normal Release CI suite ran 262 tests: 260 passed, two fresh-token acceptance cases failed,
and none were skipped. The failures are the `TrackAll` and `None` cases above; all 35 pre-existing
integration cases and the four new read/stale-token cases passed. This is failing native acceptance,
not a passing conditional-confirmation fix. The 30-minute manual LongSoak was not run.
