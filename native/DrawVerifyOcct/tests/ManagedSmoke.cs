using DrawVerify.Occt;

if (args.Length != 1)
{
    throw new ArgumentException("Expected exactly one deterministic STEP fixture path.");
}

var loader = new OcctModelLoader();
var runtime = new OcctRuntime().CheckReadiness();
if (!runtime.IsReady || runtime.OcctVersion != "8.0" || !runtime.IsStepSupported || !runtime.IsProjectionSupported || !runtime.IsMeshingSupported)
{
    throw new InvalidOperationException($"OCCT runtime is not ready: {runtime.ErrorCode} {runtime.Message}");
}
try
{
    using var missingModel = loader.LoadStep(args[0] + ".missing");
    throw new InvalidOperationException("Missing STEP input unexpectedly loaded.");
}
catch (OcctOperationException error) when (error.ErrorCode == "FileNotFound")
{
}

using var model = loader.LoadStep(args[0]);
if (model.IsNull)
{
    throw new InvalidOperationException("STEP transfer returned a null model.");
}

var summary = model.GetSummary();
var vertices = model.GetVertices();
var edges = model.GetEdges();
var faces = model.GetFaces();
var boundingBox = model.GetBoundingBox();
var faceEdges = model.GetFaceEdges(faces[0].Id);
var edgeVertices = model.GetEdgeVertices(edges[0].Id);
var edge = model.GetEdge(edges[0].Id);
var face = model.GetFace(faces[0].Id);
var validation = model.Validate();
var edgeLength = model.GetEdgeLength(edges[0].Id);
var faceArea = model.GetFaceArea(faces[0].Id);
var pointDistance = model.Distance(new OcctPoint3(0, 0, 0), new OcctPoint3(3, 4, 0));
var vertexDistance = model.MeasureVertexToVertex(vertices[0].Id, vertices[1].Id);
var front = model.GenerateProjection(OcctProjectionView.Front);
var rear = model.GenerateProjection(OcctProjectionView.Rear);
var top = model.GenerateProjection(OcctProjectionView.Top);
var bottom = model.GenerateProjection(OcctProjectionView.Bottom);
var left = model.GenerateProjection(OcctProjectionView.Left);
var right = model.GenerateProjection(OcctProjectionView.Right);
var custom = model.GenerateCustomProjection(new OcctVector3(0, 0, 1), new OcctVector3(0, 1, 0));
var mesh = model.GenerateMesh();
var meshData = model.GenerateMeshData();

if (summary.VertexCount <= 0 || edges.Count <= 0 || faces.Count <= 0)
{
    throw new InvalidOperationException("Loaded model did not provide valid topology.");
}

if (boundingBox.IsVoid || faceEdges.Count <= 0 || edgeVertices.Count <= 0 || edge.Id != edges[0].Id || face.Id != faces[0].Id || !validation.IsValid || edgeLength < 0 || faceArea <= 0 || vertexDistance < 0 || Math.Abs(pointDistance - 5) > 1e-12)
{
    throw new InvalidOperationException("Topology relationships, bounds, or measurements were not available.");
}

if (front.VisibleLines.Count + front.VisibleArcs.Count + front.VisibleCircles.Count <= 0 ||
    rear.VisibleLines.Count + rear.VisibleArcs.Count + rear.VisibleCircles.Count <= 0 ||
    top.VisibleLines.Count + top.VisibleArcs.Count + top.VisibleCircles.Count <= 0 ||
    bottom.VisibleLines.Count + bottom.VisibleArcs.Count + bottom.VisibleCircles.Count <= 0 ||
    left.VisibleLines.Count + left.VisibleArcs.Count + left.VisibleCircles.Count <= 0 ||
    right.VisibleLines.Count + right.VisibleArcs.Count + right.VisibleCircles.Count <= 0 ||
    custom.VisibleLines.Count + custom.VisibleArcs.Count + custom.VisibleCircles.Count <= 0)
{
    throw new InvalidOperationException("Required orthographic projection produced no visible geometry.");
}

try
{
    _ = model.GenerateCustomProjection(new OcctVector3(0, 0, 0), new OcctVector3(0, 1, 0));
    throw new InvalidOperationException("Invalid custom projection direction unexpectedly succeeded.");
}
catch (ArgumentException)
{
}

if (mesh.VertexCount <= 0 || mesh.TriangleCount <= 0 || meshData.Vertices.Count != mesh.VertexCount || meshData.Triangles.Count != mesh.TriangleCount)
{
    throw new InvalidOperationException("Meshing produced no triangles.");
}

for (var iteration = 0; iteration < 3; iteration++)
{
    using var reloaded = loader.LoadStep(args[0]);
    if (reloaded.GetSummary().FaceCount != summary.FaceCount)
    {
        throw new InvalidOperationException("Dispose/reload changed deterministic model topology.");
    }
}

Console.WriteLine($"unit={summary.Unit} scaleToMm={summary.UnitScaleToMillimeters} vertices={summary.VertexCount} edges={edges.Count} faces={faces.Count} triangles={mesh.TriangleCount}");
