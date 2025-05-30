﻿using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public class FastNoise
{
    private static readonly object noiseLock = new object(); // Shared lock for all native calls

    public struct OutputMinMax
    {
        public OutputMinMax(float minValue = float.PositiveInfinity, float maxValue = float.NegativeInfinity)
        {
            min = minValue;
            max = maxValue;
        }

        public OutputMinMax(float[] nativeOutputMinMax)
        {
            min = nativeOutputMinMax[0];
            max = nativeOutputMinMax[1];
        }

        public void Merge(OutputMinMax other)
        {
            min = Math.Min(min, other.min);
            max = Math.Max(max, other.max);
        }

        public float min;
        public float max;
    }

    public FastNoise(string metadataName)
    {
        if (!metadataNameLookup.TryGetValue(FormatLookup(metadataName), out mMetadataId))
        {
            throw new ArgumentException("Failed to find metadata name: " + metadataName);
        }

        mNodeHandle = fnNewFromMetadata(mMetadataId, 0); // Use default SIMD level (0 = auto-detect)
    }

    private FastNoise(IntPtr nodeHandle)
    {
        mNodeHandle = nodeHandle;
        mMetadataId = fnGetMetadataID(nodeHandle);
    }

    ~FastNoise()
    {
        fnDeleteNodeRef(mNodeHandle);
    }

    public static FastNoise FromEncodedNodeTree(string encodedNodeTree)
    {
        IntPtr nodeHandle = fnNewFromEncodedNodeTree(encodedNodeTree, 0); // Use default SIMD level

        if (nodeHandle == IntPtr.Zero)
        {
            return null;
        }

        return new FastNoise(nodeHandle);
    }

    public uint GetSIMDLevel()
    {
        return fnGetSIMDLevel(mNodeHandle);
    }

    public void Set(string memberName, float value)
    {
        Metadata.Member member;
        if (!nodeMetadata[mMetadataId].members.TryGetValue(FormatLookup(memberName), out member))
        {
            throw new ArgumentException("Failed to find member name: " + memberName);
        }

        switch (member.type)
        {
            case Metadata.Member.Type.Float:
                if (!fnSetVariableFloat(mNodeHandle, member.index, value))
                {
                    throw new ExternalException("Failed to set float value");
                }
                break;

            case Metadata.Member.Type.Hybrid:
                if (!fnSetHybridFloat(mNodeHandle, member.index, value))
                {
                    throw new ExternalException("Failed to set float value");
                }
                break;

            default:
                throw new ArgumentException(memberName + " cannot be set to a float value");
        }
    }

    public void Set(string memberName, int value)
    {
        Metadata.Member member;
        if (!nodeMetadata[mMetadataId].members.TryGetValue(FormatLookup(memberName), out member))
        {
            throw new ArgumentException("Failed to find member name: " + memberName);
        }

        if (member.type != Metadata.Member.Type.Int)
        {
            throw new ArgumentException(memberName + " cannot be set to an int value");
        }

        if (!fnSetVariableIntEnum(mNodeHandle, member.index, value))
        {
            throw new ExternalException("Failed to set int value");
        }
    }

    public void Set(string memberName, string enumValue)
    {
        Metadata.Member member;
        if (!nodeMetadata[mMetadataId].members.TryGetValue(FormatLookup(memberName), out member))
        {
            throw new ArgumentException("Failed to find member name: " + memberName);
        }

        if (member.type != Metadata.Member.Type.Enum)
        {
            throw new ArgumentException(memberName + " cannot be set to an enum value");
        }

        int enumIdx;
        if (!member.enumNames.TryGetValue(FormatLookup(enumValue), out enumIdx))
        {
            throw new ArgumentException("Failed to find enum value: " + enumValue);
        }

        if (!fnSetVariableIntEnum(mNodeHandle, member.index, enumIdx))
        {
            throw new ExternalException("Failed to set enum value");
        }
    }

    public void Set(string memberName, FastNoise nodeLookup)
    {
        Metadata.Member member;
        if (!nodeMetadata[mMetadataId].members.TryGetValue(FormatLookup(memberName), out member))
        {
            throw new ArgumentException("Failed to find member name: " + memberName);
        }

        switch (member.type)
        {
            case Metadata.Member.Type.NodeLookup:
                if (!fnSetNodeLookup(mNodeHandle, member.index, nodeLookup.mNodeHandle))
                {
                    throw new ExternalException("Failed to set node lookup");
                }
                break;

            case Metadata.Member.Type.Hybrid:
                if (!fnSetHybridNodeLookup(mNodeHandle, member.index, nodeLookup.mNodeHandle))
                {
                    throw new ExternalException("Failed to set node lookup");
                }
                break;

            default:
                throw new ArgumentException(memberName + " cannot be set to a node lookup");
        }
    }

    public OutputMinMax GenUniformGrid2D(float[] noiseOut,
                                   int xStart, int yStart,
                                   int xSize, int ySize,
                                   float frequency, int seed)
    {
        lock (noiseLock) // Serialize access to the native method
        {
            float[] minMax = new float[2];
            GCHandle noiseHandle = GCHandle.Alloc(noiseOut, GCHandleType.Pinned);
            GCHandle minMaxHandle = GCHandle.Alloc(minMax, GCHandleType.Pinned);
            try
            {
                fnGenUniformGrid2D(mNodeHandle, noiseOut, xStart, yStart, xSize, ySize, frequency, seed, minMax);
            }
            finally
            {
                noiseHandle.Free();
                minMaxHandle.Free();
            }
            return new OutputMinMax(minMax);
        }
    }

    public OutputMinMax GenUniformGrid3D(float[] noiseOut,
                                   int xStart, int yStart, int zStart,
                                   int xSize, int ySize, int zSize,
                                   float frequency, int seed)
    {
        lock (noiseLock) // Serialize access to the native method
        {
            CavesAndCaverns.CavesAndCavernsCore.ServerAPI?.Logger.Debug(
                "[FastNoise] Inside GenUniformGrid3D lock, Thread={0}",
                System.Threading.Thread.CurrentThread.ManagedThreadId
            );
            float[] minMax = new float[2];
            GCHandle noiseHandle = GCHandle.Alloc(noiseOut, GCHandleType.Pinned);
            GCHandle minMaxHandle = GCHandle.Alloc(minMax, GCHandleType.Pinned);
            try
            {
                fnGenUniformGrid3D(mNodeHandle, noiseOut, xStart, yStart, zStart, xSize, ySize, zSize, frequency, seed, minMax);
            }
            finally
            {
                noiseHandle.Free();
                minMaxHandle.Free();
            }
            return new OutputMinMax(minMax);
        }
    }

    public OutputMinMax GenUniformGrid4D(float[] noiseOut,
                                   int xStart, int yStart, int zStart, int wStart,
                                   int xSize, int ySize, int zSize, int wSize,
                                   float frequency, int seed)
    {
        lock (noiseLock) // Serialize access to the native method
        {
            float[] minMax = new float[2];
            GCHandle noiseHandle = GCHandle.Alloc(noiseOut, GCHandleType.Pinned);
            GCHandle minMaxHandle = GCHandle.Alloc(minMax, GCHandleType.Pinned);
            try
            {
                fnGenUniformGrid4D(mNodeHandle, noiseOut, xStart, yStart, zStart, wStart, xSize, ySize, zSize, wSize, frequency, seed, minMax);
            }
            finally
            {
                noiseHandle.Free();
                minMaxHandle.Free();
            }
            return new OutputMinMax(minMax);
        }
    }

    public OutputMinMax GenTileable2D(float[] noiseOut,
                                   int xSize, int ySize,
                                   float frequency, int seed)
    {
        lock (noiseLock) // Serialize access to the native method
        {
            float[] minMax = new float[2];
            GCHandle noiseHandle = GCHandle.Alloc(noiseOut, GCHandleType.Pinned);
            GCHandle minMaxHandle = GCHandle.Alloc(minMax, GCHandleType.Pinned);
            try
            {
                fnGenTileable2D(mNodeHandle, noiseOut, xSize, ySize, frequency, seed, minMax);
            }
            finally
            {
                noiseHandle.Free();
                minMaxHandle.Free();
            }
            return new OutputMinMax(minMax);
        }
    }

    public OutputMinMax GenPositionArray2D(float[] noiseOut,
                                         float[] xPosArray, float[] yPosArray,
                                         float xOffset, float yOffset,
                                         int seed)
    {
        lock (noiseLock) // Serialize access to the native method
        {
            float[] minMax = new float[2];
            GCHandle noiseHandle = GCHandle.Alloc(noiseOut, GCHandleType.Pinned);
            GCHandle xPosHandle = GCHandle.Alloc(xPosArray, GCHandleType.Pinned);
            GCHandle yPosHandle = GCHandle.Alloc(yPosArray, GCHandleType.Pinned);
            GCHandle minMaxHandle = GCHandle.Alloc(minMax, GCHandleType.Pinned);
            try
            {
                fnGenPositionArray2D(mNodeHandle, noiseOut, xPosArray.Length, xPosArray, yPosArray, xOffset, yOffset, seed, minMax);
            }
            finally
            {
                noiseHandle.Free();
                xPosHandle.Free();
                yPosHandle.Free();
                minMaxHandle.Free();
            }
            return new OutputMinMax(minMax);
        }
    }

    public OutputMinMax GenPositionArray3D(float[] noiseOut,
                                         float[] xPosArray, float[] yPosArray, float[] zPosArray,
                                         float xOffset, float yOffset, float zOffset,
                                         int seed)
    {
        lock (noiseLock) // Serialize access to the native method
        {
            float[] minMax = new float[2];
            GCHandle noiseHandle = GCHandle.Alloc(noiseOut, GCHandleType.Pinned);
            GCHandle xPosHandle = GCHandle.Alloc(xPosArray, GCHandleType.Pinned);
            GCHandle yPosHandle = GCHandle.Alloc(yPosArray, GCHandleType.Pinned);
            GCHandle zPosHandle = GCHandle.Alloc(zPosArray, GCHandleType.Pinned);
            GCHandle minMaxHandle = GCHandle.Alloc(minMax, GCHandleType.Pinned);
            try
            {
                fnGenPositionArray3D(mNodeHandle, noiseOut, xPosArray.Length, xPosArray, yPosArray, zPosArray, xOffset, yOffset, zOffset, seed, minMax);
            }
            finally
            {
                noiseHandle.Free();
                xPosHandle.Free();
                yPosHandle.Free();
                zPosHandle.Free();
                minMaxHandle.Free();
            }
            return new OutputMinMax(minMax);
        }
    }

    public OutputMinMax GenPositionArray4D(float[] noiseOut,
                                         float[] xPosArray, float[] yPosArray, float[] zPosArray, float[] wPosArray,
                                         float xOffset, float yOffset, float zOffset, float wOffset,
                                         int seed)
    {
        lock (noiseLock) // Serialize access to the native method
        {
            float[] minMax = new float[2];
            GCHandle noiseHandle = GCHandle.Alloc(noiseOut, GCHandleType.Pinned);
            GCHandle xPosHandle = GCHandle.Alloc(xPosArray, GCHandleType.Pinned);
            GCHandle yPosHandle = GCHandle.Alloc(yPosArray, GCHandleType.Pinned);
            GCHandle zPosHandle = GCHandle.Alloc(zPosArray, GCHandleType.Pinned);
            GCHandle wPosHandle = GCHandle.Alloc(wPosArray, GCHandleType.Pinned);
            GCHandle minMaxHandle = GCHandle.Alloc(minMax, GCHandleType.Pinned);
            try
            {
                fnGenPositionArray4D(mNodeHandle, noiseOut, xPosArray.Length, xPosArray, yPosArray, zPosArray, wPosArray, xOffset, yOffset, zOffset, wOffset, seed, minMax);
            }
            finally
            {
                noiseHandle.Free();
                xPosHandle.Free();
                yPosHandle.Free();
                zPosHandle.Free();
                wPosHandle.Free();
                minMaxHandle.Free();
            }
            return new OutputMinMax(minMax);
        }
    }

    public float GenSingle2D(float x, float y, int seed)
    {
        lock (noiseLock) // Serialize access to the native method
        {
            return fnGenSingle2D(mNodeHandle, x, y, seed);
        }
    }

    public float GenSingle3D(float x, float y, float z, int seed)
    {
        lock (noiseLock) // Serialize access to the native method
        {
            return fnGenSingle3D(mNodeHandle, x, y, z, seed);
        }
    }

    public float GenSingle4D(float x, float y, float z, float w, int seed)
    {
        lock (noiseLock) // Serialize access to the native method
        {
            return fnGenSingle4D(mNodeHandle, x, y, z, w, seed);
        }
    }

    private IntPtr mNodeHandle = IntPtr.Zero;
    private int mMetadataId = -1;

    public class Metadata
    {
        public struct Member
        {
            public enum Type
            {
                Float,
                Int,
                Enum,
                NodeLookup,
                Hybrid,
            }

            public string name;
            public Type type;
            public int index;
            public Dictionary<string, int> enumNames;
        }

        public int id;
        public string name;
        public Dictionary<string, Member> members;
    }

    static FastNoise()
    {
        try
        {
            // Dynamically load the DLL from the mod's native folder
            string modDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string dllPath = System.IO.Path.Combine(modDir, "native", "FastNoise.dll");
            IntPtr handle = LoadLibrary(dllPath);
            if (handle == IntPtr.Zero)
            {
                int errorCode = Marshal.GetLastWin32Error();
                throw new DllNotFoundException($"Failed to load FastNoise.dll from {dllPath}. Win32 Error Code: {errorCode}");
            }

            // Removed SIMD forcing logic; this will be handled by FastNoiseSafeInit in CavesAndCavernsCore.cs

            int metadataCount = fnGetMetadataCount();
            nodeMetadata = new Metadata[metadataCount];
            metadataNameLookup = new Dictionary<string, int>(metadataCount);

            for (int id = 0; id < metadataCount; id++)
            {
                Metadata metadata = new Metadata();

                metadata.id = id;
                metadata.name = FormatLookup(Marshal.PtrToStringAnsi(fnGetMetadataName(id)));
                metadataNameLookup.Add(metadata.name, id);

                int variableCount = fnGetMetadataVariableCount(id);
                int nodeLookupCount = fnGetMetadataNodeLookupCount(id);
                int hybridCount = fnGetMetadataHybridCount(id);
                metadata.members = new Dictionary<string, Metadata.Member>(variableCount + nodeLookupCount + hybridCount);

                for (int variableIdx = 0; variableIdx < variableCount; variableIdx++)
                {
                    Metadata.Member member = new Metadata.Member();

                    member.name = FormatLookup(Marshal.PtrToStringAnsi(fnGetMetadataVariableName(id, variableIdx)));
                    member.type = (Metadata.Member.Type)fnGetMetadataVariableType(id, variableIdx);
                    member.index = variableIdx;

                    member.name = FormatDimensionMember(member.name, fnGetMetadataVariableDimensionIdx(id, variableIdx));

                    if (member.type == Metadata.Member.Type.Enum)
                    {
                        int enumCount = fnGetMetadataEnumCount(id, variableIdx);
                        member.enumNames = new Dictionary<string, int>(enumCount);

                        for (int enumIdx = 0; enumIdx < enumCount; enumIdx++)
                        {
                            member.enumNames.Add(FormatLookup(Marshal.PtrToStringAnsi(fnGetMetadataEnumName(id, variableIdx, enumIdx))), enumIdx);
                        }
                    }

                    metadata.members.Add(member.name, member);
                }

                for (int nodeLookupIdx = 0; nodeLookupIdx < nodeLookupCount; nodeLookupIdx++)
                {
                    Metadata.Member member = new Metadata.Member();

                    member.name = FormatLookup(Marshal.PtrToStringAnsi(fnGetMetadataNodeLookupName(id, nodeLookupIdx)));
                    member.type = Metadata.Member.Type.NodeLookup;
                    member.index = nodeLookupIdx;

                    member.name = FormatDimensionMember(member.name, fnGetMetadataNodeLookupDimensionIdx(id, nodeLookupIdx));

                    metadata.members.Add(member.name, member);
                }

                for (int hybridIdx = 0; hybridIdx < hybridCount; hybridIdx++)
                {
                    Metadata.Member member = new Metadata.Member();

                    member.name = FormatLookup(Marshal.PtrToStringAnsi(fnGetMetadataHybridName(id, hybridIdx)));
                    member.type = Metadata.Member.Type.Hybrid;
                    member.index = hybridIdx;

                    member.name = FormatDimensionMember(member.name, fnGetMetadataHybridDimensionIdx(id, hybridIdx));

                    metadata.members.Add(member.name, member);
                }
                nodeMetadata[id] = metadata;
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to initialize FastNoise metadata: {ex.Message}\nInner Exception: {ex.InnerException?.Message}\nStack Trace: {ex.StackTrace}", ex);
        }
    }

    private static string FormatDimensionMember(string name, int dimIdx)
    {
        if (dimIdx >= 0)
        {
            char[] dimSuffix = new char[] { 'x', 'y', 'z', 'w' };
            name += dimSuffix[dimIdx];
        }
        return name;
    }

    private static string FormatLookup(string s)
    {
        return s.Replace(" ", "").ToLower();
    }

    static private Dictionary<string, int> metadataNameLookup;
    static private Metadata[] nodeMetadata;

    private const string NATIVE_LIB = "FastNoise"; // Simplified name since we're loading it explicitly

    [DllImport(NATIVE_LIB)]
    private static extern IntPtr fnNewFromMetadata(int id, uint simdLevel = 0);

    [DllImport(NATIVE_LIB)]
    private static extern IntPtr fnNewFromEncodedNodeTree([MarshalAs(UnmanagedType.LPStr)] string encodedNodeTree, uint simdLevel = 0);

    [DllImport(NATIVE_LIB)]
    private static extern void fnDeleteNodeRef(IntPtr nodeHandle);

    [DllImport(NATIVE_LIB)]
    private static extern uint fnGetSIMDLevel(IntPtr nodeHandle);

    [DllImport(NATIVE_LIB)]
    private static extern int fnGetMetadataID(IntPtr nodeHandle);

    [DllImport(NATIVE_LIB)]
    private static extern uint fnGenUniformGrid2D(IntPtr nodeHandle, float[] noiseOut,
                                   int xStart, int yStart,
                                   int xSize, int ySize,
                                   float frequency, int seed, float[] outputMinMax);

    [DllImport(NATIVE_LIB)]
    private static extern uint fnGenUniformGrid3D(IntPtr nodeHandle, float[] noiseOut,
                                   int xStart, int yStart, int zStart,
                                   int xSize, int ySize, int zSize,
                                   float frequency, int seed, float[] outputMinMax);

    [DllImport(NATIVE_LIB)]
    private static extern uint fnGenUniformGrid4D(IntPtr nodeHandle, float[] noiseOut,
                                   int xStart, int yStart, int zStart, int wStart,
                                   int xSize, int ySize, int zSize, int wSize,
                                   float frequency, int seed, float[] outputMinMax);

    [DllImport(NATIVE_LIB)]
    private static extern void fnGenTileable2D(IntPtr node, float[] noiseOut,
                                    int xSize, int ySize,
                                    float frequency, int seed, float[] outputMinMax);

    [DllImport(NATIVE_LIB)]
    private static extern void fnGenPositionArray2D(IntPtr node, float[] noiseOut, int count,
                                         float[] xPosArray, float[] yPosArray,
                                         float xOffset, float yOffset,
                                         int seed, float[] outputMinMax);

    [DllImport(NATIVE_LIB)]
    private static extern void fnGenPositionArray3D(IntPtr node, float[] noiseOut, int count,
                                         float[] xPosArray, float[] yPosArray, float[] zPosArray,
                                         float xOffset, float yOffset, float zOffset,
                                         int seed, float[] outputMinMax);

    [DllImport(NATIVE_LIB)]
    private static extern void fnGenPositionArray4D(IntPtr node, float[] noiseOut, int count,
                                         float[] xPosArray, float[] yPosArray, float[] zPosArray, float[] wPosArray,
                                         float xOffset, float yOffset, float zOffset, float wOffset,
                                         int seed, float[] outputMinMax);

    [DllImport(NATIVE_LIB)]
    private static extern float fnGenSingle2D(IntPtr node, float x, float y, int seed);

    [DllImport(NATIVE_LIB)]
    private static extern float fnGenSingle3D(IntPtr node, float x, float y, float z, int seed);

    [DllImport(NATIVE_LIB)]
    private static extern float fnGenSingle4D(IntPtr node, float x, float y, float z, float w, int seed);

    [DllImport(NATIVE_LIB)]
    private static extern int fnGetMetadataCount();

    [DllImport(NATIVE_LIB)]
    private static extern IntPtr fnGetMetadataName(int id);

    [DllImport(NATIVE_LIB)]
    private static extern int fnGetMetadataVariableCount(int id);

    [DllImport(NATIVE_LIB)]
    private static extern IntPtr fnGetMetadataVariableName(int id, int variableIndex);

    [DllImport(NATIVE_LIB)]
    private static extern int fnGetMetadataVariableType(int id, int variableIndex);

    [DllImport(NATIVE_LIB)]
    private static extern int fnGetMetadataVariableDimensionIdx(int id, int variableIndex);

    [DllImport(NATIVE_LIB)]
    private static extern int fnGetMetadataEnumCount(int id, int variableIndex);

    [DllImport(NATIVE_LIB)]
    private static extern IntPtr fnGetMetadataEnumName(int id, int variableIndex, int enumIndex);

    [DllImport(NATIVE_LIB)]
    private static extern bool fnSetVariableFloat(IntPtr nodeHandle, int variableIndex, float value);

    [DllImport(NATIVE_LIB)]
    private static extern bool fnSetVariableIntEnum(IntPtr nodeHandle, int variableIndex, int value);

    [DllImport(NATIVE_LIB)]
    private static extern int fnGetMetadataNodeLookupCount(int id);

    [DllImport(NATIVE_LIB)]
    private static extern IntPtr fnGetMetadataNodeLookupName(int id, int nodeLookupIndex);

    [DllImport(NATIVE_LIB)]
    private static extern int fnGetMetadataNodeLookupDimensionIdx(int id, int nodeLookupIndex);

    [DllImport(NATIVE_LIB)]
    private static extern bool fnSetNodeLookup(IntPtr nodeHandle, int nodeLookupIndex, IntPtr nodeLookupHandle);

    [DllImport(NATIVE_LIB)]
    private static extern int fnGetMetadataHybridCount(int id);

    [DllImport(NATIVE_LIB)]
    private static extern IntPtr fnGetMetadataHybridName(int id, int nodeLookupIndex);

    [DllImport(NATIVE_LIB)]
    private static extern int fnGetMetadataHybridDimensionIdx(int id, int nodeLookupIndex);

    [DllImport(NATIVE_LIB)]
    private static extern bool fnSetHybridNodeLookup(IntPtr nodeHandle, int nodeLookupIndex, IntPtr nodeLookupHandle);

    [DllImport(NATIVE_LIB)]
    private static extern bool fnSetHybridFloat(IntPtr nodeHandle, int nodeLookupIndex, float value);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);
}