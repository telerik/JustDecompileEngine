using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Security;

namespace SystemInformationHelpers
{
    public static class Framework4VersionResolver
    {
        public static FrameworkVersion GetInstalledFramework4Version()
        {
            return InstalledFrameworkData.GetInstalledFramework4Version();
        }

        public static FrameworkVersion GetFrameworkVersionByFileVersion(string assemblyFilePath)
        {
            FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(assemblyFilePath);
            if (versionInfo.FileMajorPart == 4)
            {
                if (versionInfo.FileMinorPart == 7)
                {
					if (versionInfo.FileBuildPart >= 2558)
					{
						return FrameworkVersion.v4_7_1;
					}
					else
					{
						return FrameworkVersion.v4_7;
					}
                }
                else if (versionInfo.FileMinorPart == 6)
                {
                    if (versionInfo.FileBuildPart >= 1590)
                    {
                        return FrameworkVersion.v4_6_2;
                    }
                    else if (versionInfo.FileBuildPart >= 127)
                    {
                        return FrameworkVersion.v4_6_1;
                    }
                    else
                    {
                        return FrameworkVersion.v4_6;
                    }
                }
                else if (versionInfo.FileMinorPart == 0 && versionInfo.FileBuildPart == 30319)
                {
                    if (versionInfo.FilePrivatePart >= 34209)
                    {
                        return FrameworkVersion.v4_5_2;
                    }
                    else if (versionInfo.FilePrivatePart >= 18402)
                    {
                        return FrameworkVersion.v4_5_1;
                    }
                    else if (versionInfo.FilePrivatePart > 15000)
                    {
                        return FrameworkVersion.v4_5;
                    }
                    else
                    {
                        return FrameworkVersion.v4_0;
                    }
                }
            }

            return FrameworkVersion.Unknown;
        }

        private static class InstalledFrameworkData
        {
            private static FrameworkVersion? installedFramework4Version;

            static InstalledFrameworkData()
            {
                installedFramework4Version = null;
            }

            public static FrameworkVersion GetInstalledFramework4Version()
            {
                if (!installedFramework4Version.HasValue)
                {
                    if ((int) Environment.OSVersion.Platform >= 4)
                    {
                        installedFramework4Version = GetFrameworkVersionByFileVersion(GetCLRDefault32MscorlibPath());
                        return installedFramework4Version.Value;
                    }
                    try
                    {
                        RegistryKey ndpKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                                                        .OpenSubKey("SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full\\");
                        using (ndpKey)
                        {
                            // At this line is the only chance for NullReferenceException. If it's thrown there is something wrong
                            // with the access to the subkey "SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full\\".
                            object releaseKeyAsObject = ndpKey.GetValue("Release");
                            if (releaseKeyAsObject == null)
                            {
                                installedFramework4Version = FrameworkVersion.v4_0;
                            }

                            // The following values are taken from here: https://msdn.microsoft.com/en-us/library/hh925568%28v=vs.110%29.aspx
                            int releaseKey = Convert.ToInt32(releaseKeyAsObject);

							if (releaseKey >= 461308)
							{
								installedFramework4Version = FrameworkVersion.v4_7_1;
							}
							else if (releaseKey >= 460798)
                            {
                                installedFramework4Version = FrameworkVersion.v4_7;
                            }
                            else if (releaseKey >= 394802)
                            {
                                installedFramework4Version = FrameworkVersion.v4_6_2;
                            }
                            else if (releaseKey >= 394254)
                            {
                                installedFramework4Version = FrameworkVersion.v4_6_1;
                            }
                            else if (releaseKey >= 393295)
                            {
                                installedFramework4Version = FrameworkVersion.v4_6;
                            }
                            else if (releaseKey >= 379893)
                            {
                                installedFramework4Version = FrameworkVersion.v4_5_2;
                            }
                            else if (releaseKey >= 378675)
                            {
                                installedFramework4Version = FrameworkVersion.v4_5_1;
                            }
                            else if (releaseKey >= 378389)
                            {
                                installedFramework4Version = FrameworkVersion.v4_5;
                            }
                            else
                            {
                                throw new Exception("Invalid value of Release key.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (ex is SecurityException || ex is UnauthorizedAccessException || ex is NullReferenceException)
                        {
                            installedFramework4Version = GetFrameworkVersionByFileVersion(GetCLRDefault32MscorlibPath());
                        }
                        else
                        {
                            throw;
                        }
                    }
                }

                return installedFramework4Version.Value;
            }

            private static string GetCLRDefault32MscorlibPath()
            {
                string windowsPath = Environment.GetEnvironmentVariable("windir") ?? Environment.GetEnvironmentVariable("SystemRoot");
                string clr32Path = Path.Combine(windowsPath, @"Microsoft.NET\Framework");
                string mscorlibPath = Path.Combine(clr32Path, "v4.0.30319\\mscorlib.dll");

                return mscorlibPath;
            }
        }
    }
}