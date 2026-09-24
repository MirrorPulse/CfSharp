# CfSharp Sample MSIX host

This Windows Application Packaging Project is the phase 10 Desktop Bridge host for
`CfSharp Sample`. Open `CfSharp.SampleProvider.Package.wapproj` in Visual Studio on
Windows with the Windows Application Packaging workload installed, select `x64`,
and build the package project.

The package is intentionally unsigned in source control. Before distributing a
preview or stable package, configure the CI signing identity and publisher subject
through protected secrets. The manifest uses `CN=MirrorPulse Team` as the source
publisher placeholder and must be replaced with the identity that owns the release
certificate.

The SVG is source artwork for the brand. The packaging pipeline must validate the
asset format accepted by the selected Visual Studio/MSIX toolchain and generate the
required scale-qualified PNG assets before publishing.

The package project is kept outside `CfSharp.sln` because the regular .NET CI build
does not install the Visual Studio Appx targets. Package validation belongs in a
Windows packaging job once the signing identity is provisioned.
