cmake_minimum_required(VERSION 3.16)
project(DrawVerifyOcct CXX)

# OCCT native build configuration for DrawVerify.
# Populate native/occt with the OCCT source (submodule or vendored tarball)
# and build via native/scripts/build-occt.ps1. See docs/deployment.md.

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Expected location of OCCT source tree
set(OCCT_SOURCE "${CMAKE_CURRENT_SOURCE_DIR}/occt" CACHE PATH "Path to OCCT source tree")

if(NOT EXISTS "${OCCT_SOURCE}/CMakeLists.txt")
    message(FATAL_ERROR
        "OCCT source not found at ${OCCT_SOURCE}. "
        "Check out the OCCT submodule or vendor the source before building. "
        "See docs/deployment.md.")
endif()

# Feature set (shape reading, BRep geometry — the only surface DrawVerify needs)
set(USE_TKXDESTEP ON)
set(USE_TKXSBase ON)
set(USE_TKXCAF OFF)
set(USE_TKOpenGl OFF)
set(USE_TKService OFF)

# Build only the toolkits required by the SWIG wrapper
set(BUILD_LIBRARY_TYPE Shared)

# SWIG generation happens separately via scripts/generate-wrapper.ps1
set(BUILD_MODULE_Draw OFFToolkit OFF)