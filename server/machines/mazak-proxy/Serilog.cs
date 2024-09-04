using System;

namespace Serilog
{
  public interface ILogger
  {
    public void Error(string messageTemplate, params object[] propertyValues);
    public void Debug(string messageTemplate, params object[] propertyValues);
  }

  public class Log : ILogger
  {
    public static ILogger ForContext<T>()
    {
      throw new NotImplementedException();
    }

    public static void Error(string messageTemplate, params object[] propertyValues)
    {
      throw new NotImplementedException();
    }

    public static void Error(Exception ex, string messageTemplate, params object[] propertyValues)
    {
      throw new NotImplementedException();
    }

    void ILogger.Error(string messageTemplate, params object[] propertyValues)
    {
      Log.Error(messageTemplate, propertyValues);
    }

    public static void Debug(string messageTemplate, params object[] propertyValues)
    {
      throw new NotImplementedException();
    }

    public static void Debug(Exception ex, string messageTemplate, params object[] propertyValues)
    {
      throw new NotImplementedException();
    }

    void ILogger.Debug(string messageTemplate, params object[] propertyValues)
    {
      Log.Debug(messageTemplate, propertyValues);
    }
  }
}
