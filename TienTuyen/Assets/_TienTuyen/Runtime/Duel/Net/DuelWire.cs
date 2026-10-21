using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Unity.Netcode;

namespace TienTuyen.Duel.Net
{
    /// <summary>
    /// Copies sim structs byte for byte, so the client re-simulates exactly the values the server stepped.
    /// Both ends must share the struct layout; <see cref="Fingerprint{T}"/> is exchanged at connect time so a
    /// build with a reordered, renamed or retyped field is refused instead of reading garbage.
    /// </summary>
    internal static class DuelWire
    {
        private static byte[] s_Scratch = new byte[256];

        public static int SizeOf<T>() where T : unmanaged
        {
            T value = default;
            return MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref value, 1)).Length;
        }

        public static void Write<T>(FastBufferWriter writer, in T value) where T : unmanaged
        {
            T copy = value;
            var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref copy, 1));
            if (s_Scratch.Length < bytes.Length) s_Scratch = new byte[bytes.Length];
            bytes.CopyTo(s_Scratch);
            writer.WriteBytesSafe(s_Scratch, bytes.Length);
        }

        public static bool TryRead<T>(FastBufferReader reader, out T value) where T : unmanaged
        {
            value = default;
            var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref value, 1));
            if (reader.Length - reader.Position < bytes.Length) return false;
            if (s_Scratch.Length < bytes.Length) s_Scratch = new byte[bytes.Length];
            reader.ReadBytesSafe(ref s_Scratch, bytes.Length);
            new ReadOnlySpan<byte>(s_Scratch, 0, bytes.Length).CopyTo(bytes);
            return true;
        }

        /// <summary>FNV-1a over size and every field's name and type in declaration order, nested structs included.</summary>
        public static uint Fingerprint<T>() where T : unmanaged
        {
            uint hash = 2166136261;
            Mix(ref hash, typeof(T).FullName + "/" + SizeOf<T>());
            MixFields(ref hash, typeof(T), 0);
            return hash;
        }

        private static void MixFields(ref uint hash, Type type, int depth)
        {
            if (type.IsPrimitive || type.IsEnum || depth > 8) return;
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Array.Sort(fields, (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
            foreach (var field in fields)
            {
                var fieldType = field.FieldType;
                Mix(ref hash, field.Name + ":" + (fieldType.IsEnum ? fieldType.FullName + "/" + Enum.GetUnderlyingType(fieldType).Name : fieldType.FullName));
                MixFields(ref hash, fieldType, depth + 1);
            }
        }

        private static void Mix(ref uint hash, string text)
        {
            foreach (char c in text)
            {
                hash ^= c;
                hash *= 16777619;
            }
            hash ^= 0xFF;
            hash *= 16777619;
        }
    }
}
