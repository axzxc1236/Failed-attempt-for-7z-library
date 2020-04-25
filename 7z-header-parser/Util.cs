using System;

namespace _7z_header_parser
{
    class Util
    {
        /// <summary>
        /// Give a byte array, returns something like "11-00-01-A2-8F-60"
        /// </summary>
        /// <returns></returns>
        public static string Bytes_to_hex_string(byte[] bytes)
        {
            return BitConverter.ToString(bytes);
        }

        /// <summary>
        /// Give a byte, returns something like "7A" or "00" (always two characters)
        /// </summary>
        /// <returns></returns>
        public static string Byte_to_hex_string(byte _byte)
        {
            return BitConverter.ToString(new byte[] { _byte });
        }

        public static string Bytes_to_CRC_string(byte[] bytes) {
            if (bytes.Length != 4)
                throw new Exception("Can only covert byte array that has exactly 4 bytes to CRC string");
            return Bytes_to_hex_string(bytes).Replace("-", "");
        }

        public static ulong Bytes_to_ulong(byte[] bytes)
        {
            byte[] tmp = (byte[])bytes.Clone();
            //Every windows 10 system is little endian, not sure on other systems.
            if (BitConverter.IsLittleEndian)
                Array.Reverse(tmp);
            return BitConverter.ToUInt64(tmp, 0);
        }
    }
}
