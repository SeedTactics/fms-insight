using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MazakMachineInterface
{
  public enum RegSAM
  {
    QueryValue = 0x0001,
    SetValue = 0x0002,
    CreateSubKey = 0x0004,
    EnumerateSubKeys = 0x0008,
    Notify = 0x0010,
    CreateLink = 0x0020,
    WOW64_32Key = 0x0200,
    WOW64_64Key = 0x0100,
    WOW64_Res = 0x0300,
    Read = 0x00020019,
    Write = 0x00020006,
    Execute = 0x00020019,
    AllAccess = 0x000f003f,
  }

  public static class RegHive
  {
    public static UIntPtr HKEY_LOCAL_MACHINE = new UIntPtr(0x80000002u);
  }

  public static class RegistryWOW6432
  {
    [DllImport("Advapi32.dll")]
    static extern uint RegOpenKeyEx(
      UIntPtr hKey,
      string lpSubKey,
      uint ulOptions,
      int samDesired,
      out int phkResult
    );

    [DllImport("Advapi32.dll")]
    static extern uint RegCloseKey(int hKey);

    [DllImport("advapi32.dll", EntryPoint = "RegQueryValueEx")]
    public static extern int RegQueryValueEx(
      int hKey,
      string lpValueName,
      int lpReserved,
      ref uint lpType,
      System.Text.StringBuilder lpData,
      ref uint lpcbData
    );

    public static string GetRegKey64(UIntPtr inHive, String inKeyName, String inPropertyName)
    {
      return GetRegKey(inHive, inKeyName, RegSAM.WOW64_64Key, inPropertyName);
    }

    public static string GetRegKey32(UIntPtr inHive, String inKeyName, String inPropertyName)
    {
      return GetRegKey(inHive, inKeyName, RegSAM.WOW64_32Key, inPropertyName);
    }

    public static string GetRegKey(
      UIntPtr inHive,
      String inKeyName,
      RegSAM in32or64key,
      String inPropertyName
    )
    {
      //UIntPtr HKEY_LOCAL_MACHINE = (UIntPtr)0x80000002;
      int hkey = 0;

      try
      {
        uint lResult = RegOpenKeyEx(
          RegHive.HKEY_LOCAL_MACHINE,
          inKeyName,
          0,
          (int)RegSAM.QueryValue | (int)in32or64key,
          out hkey
        );
        if (0 != lResult)
          return null;
        uint lpType = 0;
        uint lpcbData = 1024;
        StringBuilder AgeBuffer = new StringBuilder(1024);
        RegQueryValueEx(hkey, inPropertyName, 0, ref lpType, AgeBuffer, ref lpcbData);
        string Age = AgeBuffer.ToString();
        return Age;
      }
      finally
      {
        if (0 != hkey)
          RegCloseKey(hkey);
      }
    }
  }
}
