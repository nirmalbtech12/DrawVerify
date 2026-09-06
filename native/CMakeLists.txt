cmake_minimum_required(VERSION 3.20)
project(DrawVerifyOcctNative LANGUAGES CXX)
include(CTest)

# DrawVerify links dynamically to a separately installed OCCT 8.0.0 build.
# Deliberately do not add native/occt as a subdirectory: that would make it too
# easy to accidentally fold LGPL implementation code into a proprietary binary.
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

set(DRAWVERIFY_NATIVE_SOURCE_DIR "${CMAKE_CURRENT_LIST_DIR}")
set(OCCT_INSTALL_DIR "${DRAWVERIFY_NATIVE_SOURCE_DIR}/out/occt" CACHE PATH "OCCT 8.0.0 shared install")
set(DRAWVERIFY_SWIG_WRAPPER "${DRAWVERIFY_NATIVE_SOURCE_DIR}/DrawVerifyOcct/generated/DrawVerifyOcct_wrap.cxx" CACHE FILEPATH "Generated SWIG bridge")

set(OCCT_CONFIG_PATHS
  "${OCCT_INSTALL_DIR}/lib/cmake/opencascade"
  "${OCCT_INSTALL_DIR}/cmake")
if(NOT EXISTS "${OCCT_INSTALL_DIR}/lib/cmake/opencascade/OpenCASCADEConfig.cmake" AND
   NOT EXISTS "${OCCT_INSTALL_DIR}/cmake/OpenCASCADEConfig.cmake")
  message(FATAL_ERROR "OCCT 8.0.0 shared installation is absent: ${OCCT_INSTALL_DIR}. Run native/scripts/build-occt.ps1.")
endif()
if(NOT EXISTS "${DRAWVERIFY_SWIG_WRAPPER}")
  message(FATAL_ERROR "SWIG bridge is absent: ${DRAWVERIFY_SWIG_WRAPPER}. Run native/scripts/generate-wrapper.ps1.")
endif()

find_package(OpenCASCADE 8.0.0 EXACT CONFIG REQUIRED PATHS ${OCCT_CONFIG_PATHS} NO_DEFAULT_PATH)

add_library(DrawVerifyOcctNative SHARED
  "${DRAWVERIFY_NATIVE_SOURCE_DIR}/DrawVerifyOcct/src/OcctRuntime.cpp"
  "${DRAWVERIFY_NATIVE_SOURCE_DIR}/DrawVerifyOcct/src/Model.cpp"
  "${DRAWVERIFY_NATIVE_SOURCE_DIR}/DrawVerifyOcct/src/StepReader.cpp"
  "${DRAWVERIFY_NATIVE_SOURCE_DIR}/DrawVerifyOcct/src/TopologyIndex.cpp"
  "${DRAWVERIFY_NATIVE_SOURCE_DIR}/DrawVerifyOcct/src/TopologyAnalyzer.cpp"
  "${DRAWVERIFY_NATIVE_SOURCE_DIR}/DrawVerifyOcct/src/GeometryExtractor.cpp"
  "${DRAWVERIFY_NATIVE_SOURCE_DIR}/DrawVerifyOcct/src/ShapeValidator.cpp"
  "${DRAWVERIFY_NATIVE_SOURCE_DIR}/DrawVerifyOcct/src/MeasurementService.cpp"
  "${DRAWVERIFY_NATIVE_SOURCE_DIR}/DrawVerifyOcct/src/ProjectionGenerator.cpp"
  "${DRAWVERIFY_NATIVE_SOURCE_DIR}/DrawVerifyOcct/src/MeshGenerator.cpp"
  "${DRAWVERIFY_SWIG_WRAPPER}")
target_include_directories(DrawVerifyOcctNative PRIVATE "${DRAWVERIFY_NATIVE_SOURCE_DIR}/DrawVerifyOcct/include" "${OpenCASCADE_INCLUDE_DIR}")
target_link_libraries(DrawVerifyOcctNative PRIVATE ${OpenCASCADE_LIBRARIES})
set_target_properties(DrawVerifyOcctNative PROPERTIES OUTPUT_NAME DrawVerifyOcctNative)
if(UNIX AND NOT APPLE)
  set_target_properties(DrawVerifyOcctNative PROPERTIES
    BUILD_RPATH "$ORIGIN"
    INSTALL_RPATH "$ORIGIN"
    BUILD_WITH_INSTALL_RPATH TRUE)
  target_link_options(DrawVerifyOcctNative PRIVATE "-Wl,--disable-new-dtags")
endif()

add_executable(DrawVerifyOcctFixtureGenerator "${DRAWVERIFY_NATIVE_SOURCE_DIR}/DrawVerifyOcct/tests/FixtureGenerator.cpp")
target_include_directories(DrawVerifyOcctFixtureGenerator PRIVATE "${OpenCASCADE_INCLUDE_DIR}")
target_link_libraries(DrawVerifyOcctFixtureGenerator PRIVATE ${OpenCASCADE_LIBRARIES})
if(WIN32)
  # The fixture process only needs OCCT's DLL directory. Do not append the
  # host PATH here: semicolons are CMake list separators and would split the
  # cmake -E env command into invalid arguments on Windows.
  set(DRAWVERIFY_OCCT_RUNTIME_ENV "PATH=${OpenCASCADE_BINARY_DIR}")
else()
  set(DRAWVERIFY_OCCT_RUNTIME_ENV "LD_LIBRARY_PATH=${OpenCASCADE_BINARY_DIR}:$ENV{LD_LIBRARY_PATH}")
endif()
if(UNIX AND NOT APPLE)
  set_target_properties(DrawVerifyOcctFixtureGenerator PROPERTIES BUILD_RPATH "${OCCT_INSTALL_DIR}/lib")
  target_link_options(DrawVerifyOcctFixtureGenerator PRIVATE "-Wl,--disable-new-dtags")
endif()

set(DRAWVERIFY_FIXTURE_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}/fixtures")
set(DRAWVERIFY_GENERATED_FIXTURES
  "${DRAWVERIFY_FIXTURE_DIRECTORY}/cube.step"
  "${DRAWVERIFY_FIXTURE_DIRECTORY}/cylinder.step"
  "${DRAWVERIFY_FIXTURE_DIRECTORY}/plate-through-hole.step"
  "${DRAWVERIFY_FIXTURE_DIRECTORY}/stepped-block.step")
add_custom_command(
  OUTPUT ${DRAWVERIFY_GENERATED_FIXTURES}
  COMMAND "${CMAKE_COMMAND}" -E env "${DRAWVERIFY_OCCT_RUNTIME_ENV}" "$<TARGET_FILE:DrawVerifyOcctFixtureGenerator>" "${DRAWVERIFY_FIXTURE_DIRECTORY}"
  DEPENDS DrawVerifyOcctFixtureGenerator
  VERBATIM)
add_custom_target(DrawVerifyOcctFixtures ALL DEPENDS ${DRAWVERIFY_GENERATED_FIXTURES})

if(BUILD_TESTING)
  find_program(DOTNET_EXECUTABLE dotnet REQUIRED)
  set(DRAWVERIFY_STEP_SMOKE_FIXTURE "${DRAWVERIFY_NATIVE_SOURCE_DIR}/occt/data/step/screw.step")
  if(NOT EXISTS "${DRAWVERIFY_STEP_SMOKE_FIXTURE}")
    message(FATAL_ERROR "Required OCCT STEP smoke fixture is absent: ${DRAWVERIFY_STEP_SMOKE_FIXTURE}")
  endif()
  if(WIN32)
  set(DRAWVERIFY_OCCT_RUNTIME_DIRECTORY "${OpenCASCADE_BINARY_DIR}")
  else()
    set(DRAWVERIFY_OCCT_RUNTIME_DIRECTORY "${OCCT_INSTALL_DIR}/lib")
  endif()
  add_custom_target(DrawVerifyOcctManagedSmokeBuild ALL
    COMMAND "${DOTNET_EXECUTABLE}" build "${DRAWVERIFY_NATIVE_SOURCE_DIR}/DrawVerifyOcct/tests/ManagedSmoke.csproj" -c $<CONFIG> --no-restore -m:1
    COMMAND "${CMAKE_COMMAND}" -E copy_if_different "$<TARGET_FILE:DrawVerifyOcctNative>"
      "${DRAWVERIFY_NATIVE_SOURCE_DIR}/DrawVerifyOcct/tests/bin/$<CONFIG>/net9.0/$<TARGET_FILE_NAME:DrawVerifyOcctNative>"
    COMMAND "${CMAKE_COMMAND}" -E copy_directory "${DRAWVERIFY_OCCT_RUNTIME_DIRECTORY}"
      "${DRAWVERIFY_NATIVE_SOURCE_DIR}/DrawVerifyOcct/tests/bin/$<CONFIG>/net9.0"
    DEPENDS DrawVerifyOcctNative
    VERBATIM)
  add_test(
    NAME DrawVerifyOcct.ManagedSmoke
    COMMAND "${DOTNET_EXECUTABLE}" "${DRAWVERIFY_NATIVE_SOURCE_DIR}/DrawVerifyOcct/tests/bin/$<CONFIG>/net9.0/ManagedSmoke.dll"
      "${DRAWVERIFY_STEP_SMOKE_FIXTURE}")
endif()
