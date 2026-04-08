// Minimal stubs to remove Obfuz dependency while keeping compile green.
// These are no-op implementations used when obfuscation is not in use.

using System;

namespace Obfuz
{
    // Scope is used in attribute arguments; must be a compile-time constant (enum).
    public enum ObfuzScope
    {
        All = 0,
        TypeName = 1,
        MethodName = 2,
    }

    // Marker attribute to ignore obfuscation; accept optional scope arg.
    [AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = false)]
    public sealed class ObfuzIgnoreAttribute : Attribute
    {
        public ObfuzScope Scope { get; }
        public ObfuzIgnoreAttribute() { Scope = ObfuzScope.All; }
        public ObfuzIgnoreAttribute(ObfuzScope scope) { Scope = scope; }
    }

    // Base class referenced by generated encryption VM; provide virtual API surface.
    public abstract class EncryptorBase
    {
        public virtual int OpCodeCount => 0;
        public virtual int Encrypt(int value, int opts, int salt) => value;
        public virtual int Decrypt(int value, int opts, int salt) => value;

        // Helper expected by generated VM: convert byte[] to int[] key.
        // Packs 4 bytes per int in little-endian order; pads trailing bytes with zeros.
        protected static int[] ConvertToIntKey(byte[] secretKey)
        {
            if (secretKey == null) return Array.Empty<int>();
            int len = (secretKey.Length + 3) / 4;
            var key = new int[len];
            int idx = 0;
            for (int i = 0; i < len; i++)
            {
                int b0 = idx < secretKey.Length ? secretKey[idx++] : 0;
                int b1 = idx < secretKey.Length ? secretKey[idx++] : 0;
                int b2 = idx < secretKey.Length ? secretKey[idx++] : 0;
                int b3 = idx < secretKey.Length ? secretKey[idx++] : 0;
                key[i] = (b0 & 0xFF) | ((b1 & 0xFF) << 8) | ((b2 & 0xFF) << 16) | ((b3 & 0xFF) << 24);
            }
            return key;
        }
    }
}
