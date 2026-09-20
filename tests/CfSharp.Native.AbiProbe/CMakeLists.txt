cmake_minimum_required(VERSION 3.25)

project(CfSharpNativeAbiProbe LANGUAGES CXX)

if(NOT WIN32)
  message(FATAL_ERROR "CfSharp.Native.AbiProbe requires Windows.")
endif()

add_executable(CfSharp.Native.AbiProbe main.cpp)
target_compile_features(CfSharp.Native.AbiProbe PRIVATE cxx_std_20)
target_compile_definitions(
  CfSharp.Native.AbiProbe
  PRIVATE
    UNICODE
    _UNICODE
    WIN32_LEAN_AND_MEAN
    NTDDI_VERSION=NTDDI_WIN10_RS2
)
