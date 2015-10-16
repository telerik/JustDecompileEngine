using System;
using System.IO;

using Mono.Cecil;
using Mono.Cecil.AssemblyResolver;

namespace JustDecompile.EngineInfrastructure
{
    public static class CLRHelper
    {
        public static bool IsValidClrFile(string filePath)
        {
            return CLRHelper.GetDllMachineType(filePath) == CLRHelper.MachineType.CLR;
        }

        public static bool IsValidClrNotReferenceOnlyAssembly(string assemblyFile)
        {
            if (!IsValidClrFile(assemblyFile))
            {
                return false;
            }
            AssemblyDefinition moduleDef = GlobalAssemblyResolver.Instance.GetAssemblyDefinition(assemblyFile);

            if (moduleDef != null && moduleDef.MainModule != null && moduleDef.MainModule.IsReferenceAssembly())
            {
                return false;
            }
            return true;
        }

        private static MachineType GetDllMachineType(string dllPath)
        {
            if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath))
            {
                return MachineType.Unknown;
            }
            var resolver = new DefaultAssemblyResolver(GlobalAssemblyResolver.CurrentAssemblyPathCache);

            AssemblyDefinition assemblyDefinition = resolver.GetAssemblyDefinition(dllPath);

            return assemblyDefinition == null ? MachineType.Unknown : MachineType.CLR;
        }

        private static MachineType OldGetDllMachineType(string dllPath)
        {
            if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath))
            {
                return MachineType.Unknown;
            }
            try
            {
                using (var fileStream = File.OpenRead(dllPath))
                {
                    using (var binaryReader = new BinaryReader(fileStream))
                    {
                        if (fileStream.Length < 128)
                        {
                            return MachineType.Unknown;
                        }
                        if (binaryReader.ReadUInt16() != 0x5a4d)
                        {
                            return MachineType.Unknown;
                        }
                        fileStream.Seek(58, SeekOrigin.Current);

                        fileStream.Position = binaryReader.ReadUInt32();

                        if (binaryReader.ReadUInt32() != 0x00004550)
                        {
                            return MachineType.Unknown;
                        }
                        binaryReader.ReadUInt32();

                        fileStream.Seek(14, SeekOrigin.Current);

                        binaryReader.ReadUInt16();

                        // Magic
                        bool pe64 = binaryReader.ReadUInt16() == 0x20b;

                        fileStream.Seek(66, SeekOrigin.Current);

                        binaryReader.ReadUInt16();

                        fileStream.Seek(pe64 ? 90 : 74, SeekOrigin.Current);

                        binaryReader.ReadUInt64();

                        fileStream.Seek(56, SeekOrigin.Current);

                        uint virtualAddress = binaryReader.ReadUInt32();
                        uint size = binaryReader.ReadUInt32();

                        if (virtualAddress == 0 && size == 0)
                        {
                            return MachineType.Unknown;
                        }
                    }
                }
                return MachineType.CLR;
            }
            catch (Exception)
            {
                return MachineType.Unknown;
            }
        }

        public enum MachineType
        {
            CLR,
            Unknown
        }
    }
}
