using DrawVerify.Core.Models;
namespace DrawVerify.Core.Interfaces;

/// <summary>
/// Authenticates users and authorizes workflow actions.
/// </summary>
public interface IAuthenticationService
{
    User? CurrentUser { get; }
    bool CanExtractMeasurements { get; }
}
