// Copyright (c) 2022 - 2023 AccelByte Inc. All Rights Reserved.
// This is licensed software from AccelByte Inc, for limitations
// and restrictions contact your company contract manager.

using System;
using System.Runtime.InteropServices;

public static class ABNetUtilities
{
    public static byte[] Serialize<T>(T packetT)
    {
        if (packetT is null)
        {
            throw new Exception("the packet is null.");
        }

        int size = Marshal.SizeOf(typeof(T));
        if (0 >= size)
        {
            throw new Exception("the packet size is zero.");
        }
        var array = new byte[size];
        var ptr = Marshal.AllocHGlobal(size);
        Marshal.StructureToPtr(packetT, ptr, false);
        Marshal.Copy(ptr, array, 0, size);
        Marshal.FreeHGlobal(ptr);

        return array;
    }

    public static T Deserialize<T>(byte[] inData)
    {
        if (inData is null)
        {
            throw new Exception("the packet is null.");
        }

        int size = Marshal.SizeOf(typeof(T));
        if (size > inData.Length)
        {
            throw new Exception("the size of the converted packet is wrong.");
        }

        var ptr = Marshal.AllocHGlobal(size);
        Marshal.Copy(inData, 0, ptr, size);
        var obj = (T)Marshal.PtrToStructure(ptr, typeof(T));
        Marshal.FreeHGlobal(ptr);

        return obj;
    }
}
