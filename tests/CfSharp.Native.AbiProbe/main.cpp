#include <cfapi.h>

#include <cstddef>
#include <iostream>

int main()
{
    // The probe emits machine-readable ABI facts obtained from the active
    // Windows SDK. Managed tests will compare these values with the bindings.
    std::cout << "{\n"
              << "  \"pointerSize\": " << sizeof(void*) << ",\n"
              << "  \"cfCallbackInfoSize\": " << sizeof(CF_CALLBACK_INFO) << ",\n"
              << "  \"cfOperationInfoSize\": " << sizeof(CF_OPERATION_INFO) << ",\n"
              << "  \"cfOperationParametersSize\": " << sizeof(CF_OPERATION_PARAMETERS) << "\n"
              << "}\n";

    return 0;
}
