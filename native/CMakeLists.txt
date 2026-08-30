cmake_minimum_required(VERSION 3.20)
project(DrawVerifyOcctNative LANGUAGES CXX)
include(CTest)

# DrawVerify links dynamically to a separately installed OCCT 8.0.0 build.
# Deliberately do not add native/occt as a subdirectory: that would make it too
# easy to accidentally fold LGPL implementation code into a proprietary binary.
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

set(OCCT_INSTALL_DIR "${CMAKE_CURRENT_SOURCE_DIR}/out/occt" CACHE PATH "OCCT 8.0.0 shared install")
set(DRAWVERIFY_SWIG_WRAPPER "${CMAKE_CURRENT_SOURCE_DIR}/DrawVerifyOcct/generated/DrawVerifyOcct_wrap.cxx" CACHE FILEPATH "Generated SWIG bridge")

if(NOT EXISTS "${OCCT_INSTALL_DIR}/lib/cmake/opencascade/OpenCASCADEConfig.cmake")
  message(FATAL_ERROR "OCCT 8.0.0 shared installation is absent: ${OCCT_INSTALL_DIR}. Run native/scripts/build-occt.ps1.")
endif()
if(NOT EXISTS "${DRAWVERIFY_SWIG_WRAPPER}")
  message(FATAL_ERROR "SWIG bridge is absent: ${DRAWVERIFY_SWIG_WRAPPER}. Run native/scripts/generate-wrapper.ps1.")
endif()

find_package(OpenCASCADE 8.0.0 EXACT CONFIG REQUIRED PATHS "${OCCT_INSTALL_DIR}/lib/cmake/opencascade" NO_DEFAULT_PATH)

add_library(DrawVerifyOcctNative SHARED
  DrawVerifyOcct/src/OcctRuntime.cpp
  DrawVerifyOcct/src/Model.cpp
  DrawVerifyOcct/src/StepReader.cpp
  DrawVerifyOcct/src/TopologyIndex.cpp
  DrawVerifyOcct/src/TopologyAnalyzer.cpp
  DrawVerifyOcct/src/GeometryExtractor.cpp
  DrawVerifyOcct/src/ShapeValidator.cpp
  DrawVerifyOcct/src/MeasurementService.cpp
  DrawVerifyOcct/src/ProjectionGenerator.cpp
  DrawVerifyOcct/src/MeshGenerator.cpp
  "${DRAWVERIFY_SWIG_WRAPPER}")
target_include_directories(DrawVerifyOcctNative PRIVATE DrawVerifyOcct/include "${OCCT_INSTALL_DIR}/include/opencascade-8.0.0")
target_link_libraries(DrawVerifyOcctNative PRIVATE ${OpenCASCADE_LIBRARIES})
set_target_properties(DrawVerifyOcctNative PROPERTIES OUTPUT_NAME DrawVerifyOcctNative)
if(UNIX AND NOT APPLE)
  set_target_properties(DrawVerifyOcctNative PROPERTIES
    BUILD_RPATH "$ORIGIN"
    INSTALL_RPATH "$ORIGIN"
    BUILD_WITH_INSTALL_RPATH TRUE)
  target_link_options(DrawVerifyOcctNative PRIVATE "-Wl,--disable-new-dtags")
endif()

add_executable(DrawVerifyOcctFixtureGenerator DrawVerifyOcct/tests/FixtureGenerator.cpp)
target_include_directories(DrawVerifyOcctFixtureGenerator PRIVATE "${OCCT_INSTALL_DIR}/include/opencascade-8.0.0")
target_link_libraries(DrawVerifyOcctFixtureGenerator PRIVATE ${OpenCASCADE_LIBRARIES})
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
  COMMAND "$<TARGET_FILE:DrawVerifyOcctFixtureGenerator>" "${DRAWVERIFY_FIXTURE_DIRECTORY}"
  DEPENDS DrawVerifyOcctFixtureGenerator
  VERBATIM)
add_custom_target(DrawVerifyOcctFixtures ALL DEPENDS ${DRAWVERIFY_GENERATED_FIXTURES})

if(BUILD_TESTING)
  find_program(DOTNET_EXECUTABLE dotnet REQUIRED)
  set(DRAWVERIFY_STEP_SMOKE_FIXTURE "${CMAKE_CURRENT_SOURCE_DIR}/occt/data/step/screw.step")
  if(NOT EXISTS "${DRAWVERIFY_STEP_SMOKE_FIXTURE}")
    message(FATAL_ERROR "Required OCCT STEP smoke fixture is absent: ${DRAWVERIFY_STEP_SMOKE_FIXTURE}")
  endif()
  if(WIN32)
    set(DRAWVERIFY_OCCT_RUNTIME_DIRECTORY "${OCCT_INSTALL_DIR}/bin")
  else()
    set(DRAWVERIFY_OCCT_RUNTIME_DIRECTORY "${OCCT_INSTALL_DIR}/lib")
  endif()
  add_custom_target(DrawVerifyOcctManagedSmokeBuild ALL
    COMMAND "${DOTNET_EXECUTABLE}" build "${CMAKE_CURRENT_SOURCE_DIR}/DrawVerifyOcct/tests/ManagedSmoke.csproj" --no-restore -m:1
    COMMAND "${CMAKE_COMMAND}" -E copy_if_different "$<TARGET_FILE:DrawVerifyOcctNative>"
      "${CMAKE_CURRENT_SOURCE_DIR}/DrawVerifyOcct/tests/bin/Debug/net9.0/$<TARGET_FILE_NAME:DrawVerifyOcctNative>"
    COMMAND "${CMAKE_COMMAND}" -E copy_directory "${DRAWVERIFY_OCCT_RUNTIME_DIRECTORY}"
      "${CMAKE_CURRENT_SOURCE_DIR}/DrawVerifyOcct/tests/bin/Debug/net9.0"
    DEPENDS DrawVerifyOcctNative
    VERBATIM)
  add_test(
    NAME DrawVerifyOcct.ManagedSmoke
    COMMAND "${DOTNET_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/DrawVerifyOcct/tests/bin/Debug/net9.0/ManagedSmoke.dll"
      "${DRAWVERIFY_STEP_SMOKE_FIXTURE}")
endif()
