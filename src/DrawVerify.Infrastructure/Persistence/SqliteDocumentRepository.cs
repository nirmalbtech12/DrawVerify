using Microsoft.Data.Sqlite;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
namespace DrawVerify.Infrastructure.Persistence;

/// <summary>
/// SQLite persistence for documents, implementing IDocumentRepository.
/// </summary>
public sealed class SqliteDocumentRepository : IDocumentRepository, IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection _connection;
    public SqliteDocumentRepository(string databasePath = ":memory:")
    {
        _connectionString = databasePath == ":memory:" ? "Data Source=:memory:;Cache=Shared" : $"Data Source={databasePath}";
        _connection = new SqliteConnection(_connectionString);
        _connection.Open();
        using var command = _connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS Documents (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, DocumentType INTEGER NOT NULL, UploadStatus INTEGER NOT NULL, CurrentVersionId TEXT NULL, GeometryStatus INTEGER NOT NULL, GeometryError TEXT NULL); CREATE TABLE IF NOT EXISTS DocumentVersions (Id TEXT PRIMARY KEY, DocumentId TEXT NOT NULL, VersionNumber INTEGER NOT NULL, StoredFilePath TEXT NOT NULL, GeometryStatus INTEGER NOT NULL, GeometryError TEXT NULL); CREATE TABLE IF NOT EXISTS GeometryData (DocumentVersionId TEXT PRIMARY KEY, Volume REAL NULL, MinX REAL NOT NULL, MinY REAL NOT NULL, MinZ REAL NOT NULL, MaxX REAL NOT NULL, MaxY REAL NOT NULL, MaxZ REAL NOT NULL, FaceCount INTEGER NOT NULL, EdgeCount INTEGER NOT NULL, VertexCount INTEGER NOT NULL, Unit TEXT NOT NULL, Uncertainty TEXT NULL); CREATE TABLE IF NOT EXISTS FeatureData (Id INTEGER PRIMARY KEY AUTOINCREMENT, DocumentVersionId TEXT NOT NULL, FeatureType TEXT NOT NULL, NominalDimensions TEXT NOT NULL, Unit TEXT NOT NULL, LowerTolerance REAL NULL, UpperTolerance REAL NULL, Uncertainty TEXT NULL);";
        command.ExecuteNonQuery();
    }
    public void Save(Document document, DocumentVersion version)
    {
        var c = Open(); using var tx = c.BeginTransaction();
        Execute(c, tx, "INSERT OR REPLACE INTO Documents VALUES ($id,$name,$type,$upload,$version,$status,$error)", ("$id", document.Id.ToString()), ("$name", document.Name), ("$type", (int)document.DocumentType), ("$upload", (int)document.UploadStatus), ("$version", version.Id.ToString()), ("$status", (int)document.GeometryStatus), ("$error", (object?)document.GeometryError ?? DBNull.Value));
        Execute(c, tx, "INSERT OR REPLACE INTO DocumentVersions VALUES ($id,$doc,$number,$path,$status,$error)", ("$id", version.Id.ToString()), ("$doc", document.Id.ToString()), ("$number", version.VersionNumber), ("$path", version.StoredFilePath), ("$status", (int)version.GeometryStatus), ("$error", (object?)version.GeometryError ?? DBNull.Value)); tx.Commit();
    }
    public Document? Get(Guid id) { var c = Open(); using var q = c.CreateCommand(); q.CommandText = "SELECT Name,DocumentType,UploadStatus,CurrentVersionId,GeometryStatus,GeometryError FROM Documents WHERE Id=$id"; q.Parameters.AddWithValue("$id", id.ToString()); using var r = q.ExecuteReader(); return r.Read() ? new Document { Id=id, Name=r.GetString(0), DocumentType=(DocumentType)r.GetInt32(1), UploadStatus=(UploadStatus)r.GetInt32(2), CurrentVersionId=r.IsDBNull(3)?Guid.Empty:Guid.Parse(r.GetString(3)), GeometryStatus=(GeometryStatus)r.GetInt32(4), GeometryError=r.IsDBNull(5)?null:r.GetString(5) } : null; }
    public DocumentVersion? GetCurrentVersion(Guid documentId) { var c = Open(); using var q = c.CreateCommand(); q.CommandText = "SELECT Id,VersionNumber,StoredFilePath,GeometryStatus,GeometryError FROM DocumentVersions WHERE DocumentId=$id ORDER BY VersionNumber DESC LIMIT 1"; q.Parameters.AddWithValue("$id", documentId.ToString()); using var r=q.ExecuteReader(); return r.Read()?new DocumentVersion { Id=Guid.Parse(r.GetString(0)), DocumentId=documentId, VersionNumber=r.GetInt32(1), StoredFilePath=r.GetString(2), GeometryStatus=(GeometryStatus)r.GetInt32(3), GeometryError=r.IsDBNull(4)?null:r.GetString(4)}:null; }
    public void ReplaceMeasurements(Guid versionId, GeometryMeasurement? geometry, IReadOnlyCollection<FeatureMeasurement> features, GeometryStatus status, string? error)
    { var c=Open(); using var tx=c.BeginTransaction(); try { Execute(c,tx,"DELETE FROM GeometryData WHERE DocumentVersionId=$id",("$id",versionId.ToString())); Execute(c,tx,"DELETE FROM FeatureData WHERE DocumentVersionId=$id",("$id",versionId.ToString())); if(geometry is not null) Execute(c,tx,"INSERT INTO GeometryData VALUES($id,$volume,$minx,$miny,$minz,$maxx,$maxy,$maxz,$faces,$edges,$vertices,$unit,$uncertainty)",("$id",versionId.ToString()),("$volume",(object?)geometry.Volume??DBNull.Value),("$minx",geometry.MinimumX),("$miny",geometry.MinimumY),("$minz",geometry.MinimumZ),("$maxx",geometry.MaximumX),("$maxy",geometry.MaximumY),("$maxz",geometry.MaximumZ),("$faces",geometry.FaceCount),("$edges",geometry.EdgeCount),("$vertices",geometry.VertexCount),("$unit",geometry.Unit),("$uncertainty",(object?)geometry.Uncertainty??DBNull.Value)); foreach(var f in features) Execute(c,tx,"INSERT INTO FeatureData(DocumentVersionId,FeatureType,NominalDimensions,Unit,LowerTolerance,UpperTolerance,Uncertainty) VALUES($id,$type,$dimensions,$unit,$lower,$upper,$uncertainty)",("$id",versionId.ToString()),("$type",f.FeatureType),("$dimensions",System.Text.Json.JsonSerializer.Serialize(f.NominalDimensions)),("$unit",f.Unit),("$lower",(object?)f.LowerTolerance??DBNull.Value),("$upper",(object?)f.UpperTolerance??DBNull.Value),("$uncertainty",(object?)f.Uncertainty??DBNull.Value)); Execute(c,tx,"UPDATE DocumentVersions SET GeometryStatus=$status,GeometryError=$error WHERE Id=$id",("$status",(int)status),("$error",(object?)error??DBNull.Value),("$id",versionId.ToString())); tx.Commit(); } catch { tx.Rollback(); throw; } }
    private SqliteConnection Open()=>_connection;
    public void Dispose() => _connection.Dispose();
    private static void Execute(SqliteConnection c, SqliteTransaction tx, string sql, params (string,object)[] values){using var q=c.CreateCommand();q.Transaction=tx;q.CommandText=sql;foreach(var (name,value) in values)q.Parameters.AddWithValue(name,value);q.ExecuteNonQuery();}
}
