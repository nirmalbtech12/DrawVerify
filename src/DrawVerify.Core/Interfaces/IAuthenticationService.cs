using DrawVerify.Core.Models;
<<<<<<< HEAD
=======

>>>>>>> 642060c (KAN-19: implementation)
namespace DrawVerify.Core.Interfaces;

/// <summary>
/// Authenticates users and authorizes workflow actions.
/// </summary>
public interface IAuthenticationService
{
    User? CurrentUser { get; }
<<<<<<< HEAD
    bool CanExtractMeasurements { get; }
=======
    bool IsAuthenticated { get; }
    bool IsMaker { get; }
>>>>>>> 642060c (KAN-19: implementation)
}
