namespace DrawVerify.Core.Interfaces;

public interface ICentralizedLogger
{
    void Log(Exception exception, string operation, string context);
}
