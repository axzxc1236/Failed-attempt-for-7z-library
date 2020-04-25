using Force.Crc32;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace _7z_header_parser
{
    public class _7z_parser
    {
        private BinaryReader archive_file_reader;

        public byte _Archive_version_major, _Archive_version_minor;
        public string _Archive_version_string;
        public string _StartHeaderCRC = "";
        public UInt64 _NextHeaderOffset, _NextHeaderOffsize;
        public byte[] _NextHeaderOffset_in_bytes, _NextHeaderOffsize_in_bytes;
        public string _NextHeaderCRC = "";
        public List<ArchiveProperty> _Archive_Properties;
        public StreamsInfo _Additional_Streams_Info, _Main_Streams_Info, _Encoded_Header;

        public _7z_parser(string filepath)
        {
            //opens xxx.7z passed in parameter
            Console.WriteLine("[7z-parser] specified file: " + filepath);
            archive_file_reader = new BinaryReader(File.Open(filepath, FileMode.Open));
            Console.WriteLine("[7z-parser] reader opened");

            //pretty much self-explanatory, covers "SignatureHeader" section in 7zFormat.txt (line 171~188)
            read_signature_and_archive_version();
            read_StartHeader();

            //Move position of archive_file_reader
            //will be a problem when _NextHeaderOffset is bigger than "long.MaxValue - 32" but.......... it requires a sinle 1EB .7z file if I do math correctly
            //So... this program will break if you have a 1EB .7z file and I don't want to fix this in... foreseeable future
            archive_file_reader.BaseStream.Position += (long)_NextHeaderOffset;

            Property_IDs flag = (Property_IDs)archive_file_reader.ReadByte();
            if (flag == Property_IDs.kHeader)
                read_Header();
            else if (flag == Property_IDs.kEncodedHeader)
                _Encoded_Header = read_Streams_info();
            else
                throw new ParseError("Expected kHeader or kEncodedHeader, but got 0x" + Util.Byte_to_hex_string((byte)flag) + "\n" +
                                     "Stream position = " + archive_file_reader.BaseStream.Position);
        }

        /// <summary>
        /// covers line 173~180 in 7zFormat.txt
        /// </summary>
        private void read_signature_and_archive_version() {
            //Check for kSignature  (line 173 in 7zFormat.txt)
            Console.WriteLine("[7z-parser] Check for kSignature  (line 173 in 7zFormat.txt)");
            bool SignaturerCheck = Enumerable.SequenceEqual(archive_file_reader.ReadBytes(6), new byte[6] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C });
            assert(SignaturerCheck, "[7z-parser] 7z Signature not found");
            Console.WriteLine("[7z-parser] Signature found");

            //get Archive version  (line 175~179 in 7zFormat.txt)
            Console.WriteLine("[7z-parser] Read archive version");
            _Archive_version_major = archive_file_reader.ReadByte();
            _Archive_version_minor = archive_file_reader.ReadByte();
            _Archive_version_string = _Archive_version_major.ToString() + _Archive_version_minor.ToString();
            Console.WriteLine("[7z-parser] Archive version is " + _Archive_version_string);
        }

        /// <summary>
        /// covers line 181~188 in 7zFormat.txt
        /// </summary>
        private void read_StartHeader() {
            //get StartHeaderCRC  (line 181 in 7zFormat.txt)
            Console.WriteLine("[7z-parser] Read StartHeaderCRC");
            byte[] _StartHeaderCRC_in_bytes = archive_file_reader.ReadBytes(4);
            Array.Reverse(_StartHeaderCRC_in_bytes);
            _StartHeaderCRC = Util.Bytes_to_CRC_string(_StartHeaderCRC_in_bytes);
            Console.WriteLine("[7z-parser] StartHeaderCRC = " + _StartHeaderCRC);

            //read StartHeader
            Console.WriteLine("[7z-parser] Read StartHeader");
            byte[] startHeader = archive_file_reader.ReadBytes(20);

            //Validate integrity of StartHeader
            Console.WriteLine("[7z-parser] validate StartHeader");
            assert_CRC32(startHeader, _StartHeaderCRC);
            Console.WriteLine("[7z-parser] passed, CRC32 does match");

            //get NextHeaderOffset  (line 185 in 7zFormat.txt)
            _NextHeaderOffset_in_bytes = new byte[8];
            Array.Copy(startHeader, 0, _NextHeaderOffset_in_bytes, 0, 8);
            Array.Reverse(_NextHeaderOffset_in_bytes);
            _NextHeaderOffset = Util.Bytes_to_ulong(_NextHeaderOffset_in_bytes);
            Console.WriteLine($"[7z-parser] NextHeaderOffset = {_NextHeaderOffset} ({Util.Bytes_to_hex_string(_NextHeaderOffset_in_bytes)})");

            //get NextHeaderOffsize  (line 186 in 7zFormat.txt)
            _NextHeaderOffsize_in_bytes = new byte[8];
            Array.Copy(startHeader, 8, _NextHeaderOffsize_in_bytes, 0, 8);
            Array.Reverse(_NextHeaderOffsize_in_bytes);
            _NextHeaderOffsize = Util.Bytes_to_ulong(_NextHeaderOffsize_in_bytes);
            Console.WriteLine($"[7z-parser] NextHeaderOffsize = {_NextHeaderOffsize} ({Util.Bytes_to_hex_string(_NextHeaderOffsize_in_bytes)})");

            //get NextHeaderCRC  (line 187 in 7zFormat.txt)
            byte[] _NextHeaderCRC_in_bytes = new byte[4];
            Array.Copy(startHeader, 16, _NextHeaderCRC_in_bytes, 0, 4);
            Array.Reverse(_NextHeaderCRC_in_bytes);
            _NextHeaderCRC = Util.Bytes_to_CRC_string(_NextHeaderCRC_in_bytes);
            Console.WriteLine("[7z-parser] NextHeaderCRC = " + _NextHeaderCRC);
        }

        /// <summary>
        /// covers "Header" section in 7zFormat.txt (line 435~457)
        /// </summary>
        private void read_Header() {
            Console.WriteLine("[7z-parser] read Header");
            Property_IDs flag = (Property_IDs)archive_file_reader.ReadByte();

            //optional ArchiveProperties
            if (flag == Property_IDs.kArchiveProperties)
            {
                _Archive_Properties = read_Archive_Properties();
                flag = (Property_IDs)archive_file_reader.ReadByte();
            }

            //optional AdditionalStreamsInfo
            if (flag == Property_IDs.kAdditionalStreamsInfo)
            {
                _Additional_Streams_Info= read_Streams_info();
                flag = (Property_IDs)archive_file_reader.ReadByte();
            }

            //optional MainStreamsInfo
            if (flag == Property_IDs.kMainStreamsInfo)
            {
                _Main_Streams_Info = read_Streams_info();
                flag = (Property_IDs)archive_file_reader.ReadByte();
            }

            //Check kEnd
            assert(flag == Property_IDs.kEnd,
                   "Expected kEnd (0x00), but got 0x" + Util.Byte_to_hex_string((byte)flag) + "\n" +
                   "Stream position (decimal) = " + archive_file_reader.BaseStream.Position);
            Console.WriteLine("[7z-parser] read Header complete");
        }

        /// <summary>
        /// covers "ArchiveProperties" section in 7zFormat.txt (line 194~204)
        /// </summary>
        private List<ArchiveProperty> read_Archive_Properties()
        {
            List<ArchiveProperty> list = new List<ArchiveProperty>();
            while (true) {
                Property_IDs PropertyType = (Property_IDs)archive_file_reader.ReadByte();
                if (PropertyType == Property_IDs.kEnd)
                    return list;
                ArchiveProperty entry = new ArchiveProperty();
                entry.PropertyType = PropertyType;
                entry.PropertyData = archive_file_reader.ReadBytes((int)read_7z_UInt64()); //might crash with big PropertySize
                Array.Reverse(entry.PropertyData);
                list.Add(entry);

                //debug message
                Console.WriteLine("[7z-parser] archive property type = " + Util.Byte_to_hex_string((byte)entry.PropertyType) +
                                  ", PropertySize = " + entry.PropertyData.Length +
                                  ", PropertyData = " + Util.Bytes_to_hex_string(entry.PropertyData));
            }
        }

        /// <summary>
        /// covers "Streams Info" section in 7zFormat.txt (line 341~358)
        /// </summary>
        private StreamsInfo read_Streams_info()
        {
            Console.WriteLine("[7z-parser] read Streams Info");

            StreamsInfo streams_info = new StreamsInfo();
            Property_IDs flag = (Property_IDs)archive_file_reader.ReadByte();

            //handles optional PackInfo
            if (flag == Property_IDs.kPackInfo)
            {
                streams_info.packInfo = read_Pack_Info();
                flag = (Property_IDs)archive_file_reader.ReadByte();
            }
            else
            {
                streams_info.packInfo = null;
            }

            //handles optional CodersInfo
            if (flag == Property_IDs.kUnPackInfo)
            {
                streams_info.codersInfo = read_Coders_Info();
                flag = (Property_IDs)archive_file_reader.ReadByte();
            }
            else
            {
                streams_info.codersInfo = null;
            }

            //handles optional PackInfo
            if (flag == Property_IDs.kSubStreamsInfo)
            {
                streams_info.subStreamsInfo = read_SubStreams_info();
                flag = (Property_IDs)archive_file_reader.ReadByte();
            }
            else
            {
                streams_info.subStreamsInfo = null;
            }
            //Check kEnd
            assert(flag == Property_IDs.kEnd,
                   "Expected kEnd (0x00), but got 0x" + Util.Byte_to_hex_string((byte)flag) + "\n" +
                   "Stream position (decimal) = " + archive_file_reader.BaseStream.Position);
            Console.WriteLine("[7z-parser] read Header complete");
            return streams_info;
        }

        /// <summary>
        /// covers "PackInfo" section in 7zFormat.txt (line 218~234)
        /// </summary>
        private PackInfo read_Pack_Info()
        {
            Console.WriteLine("[7z-parser] read Pack Info");

            PackInfo pack_info = new PackInfo();
            pack_info.PackPos = read_7z_UInt64();
            Console.WriteLine("PackPos = " + pack_info.PackPos);
            pack_info.NumPackStreams = read_7z_UInt64();
            Console.WriteLine("NumPackStreams = " + pack_info.NumPackStreams);

            Property_IDs flag = (Property_IDs) archive_file_reader.ReadByte();

            //handles optional PackSizes
            if (flag == Property_IDs.kSize)
            {
                Console.WriteLine("read KSize");
                pack_info.PackSizes = new ulong[pack_info.NumPackStreams];
                for (ulong i = 0; i < pack_info.NumPackStreams; i++)
                {
                    pack_info.PackSizes[i] = read_7z_UInt64();
                    Console.WriteLine("PackSizes[" + i + "] = " + pack_info.PackSizes[i]);
                }
                flag = (Property_IDs)archive_file_reader.ReadByte();
            }
            else
            {
                pack_info.PackSizes = null;
            }

            //handles optional PackStreamDigests
            if (flag == Property_IDs.kCRC)
            {
                pack_info.PackStreamDigests = new string[pack_info.NumPackStreams];
                byte[] CRC_in_bytes;
                for (ulong i = 0; i < pack_info.NumPackStreams; i++)
                {
                    CRC_in_bytes = archive_file_reader.ReadBytes(4);
                    Array.Reverse(CRC_in_bytes);
                    pack_info.PackStreamDigests[i] = Util.Bytes_to_CRC_string(CRC_in_bytes);
                }
                flag = (Property_IDs)archive_file_reader.ReadByte();
            }
            else
            {
                pack_info.PackSizes = null;
            }

            //Check kEnd
            assert(flag == Property_IDs.kEnd,
                   "Expected kEnd (0x00), but got 0x" + Util.Byte_to_hex_string((byte)flag) + "\n" +
                   "Stream position (decimal) = " + archive_file_reader.BaseStream.Position);
            Console.WriteLine("[7z-parser] read Pack Info complete");

            return pack_info;
        }

        /// <summary>
        /// covers "Coders Info" section in 7zFormat.txt (line 281~312)
        /// </summary>
        private CodersInfo read_Coders_Info()
        {
            Console.WriteLine("[7z-parser] read Coders Info");

            CodersInfo coders_info = new CodersInfo();
            Property_IDs flag = (Property_IDs) archive_file_reader.ReadByte();
            assert(flag == Property_IDs.kFolder,
                   "Expected kFolder (0x0B), but got 0x" + Util.Byte_to_hex_string((byte)flag) + "\n" +
                   "Stream position (decimal) = " + archive_file_reader.BaseStream.Position);

            //read NumFolders (line 288 in 7zFormat.txt)
            coders_info.NumFolders = read_7z_UInt64();

            //read External (line 289~296 in 7zFormat.txt)
            coders_info.External = archive_file_reader.ReadBoolean();
            Console.WriteLine("External = " + coders_info.External.ToString());
            if (!coders_info.External)
            {
                coders_info.Folders = new Folder[coders_info.NumFolders];
                Console.WriteLine("position = 0x" + archive_file_reader.BaseStream.Position.ToString("X"));
                for (ulong i = 0; i < coders_info.NumFolders; i++) {
                    coders_info.Folders[i] = read_Folder();
                }
                Console.WriteLine("position = 0x" + archive_file_reader.BaseStream.Position.ToString("X"));
            }
            else {
                coders_info.Folders = new Folder[0];
                coders_info.DataStreamIndex = read_7z_UInt64();
            }

            //kCodersUnPackSize (line 299 in 7zFormat.txt)
            flag = (Property_IDs)archive_file_reader.ReadByte();
            assert(flag == Property_IDs.kCodersUnPackSize,
                   "Expected kCodersUnPackSize (0x0C), but got 0x" + Util.Byte_to_hex_string((byte)flag) + "\n" +
                   "Stream position (decimal) = " + archive_file_reader.BaseStream.Position);

            //read UnPackSize (line 300~302 in 7zFormat.txt)
            coders_info.UnPackSizes = new List<ulong>();
            foreach (Folder f in coders_info.Folders) {
                for (ulong i = 0; i < f.NumOutStreams; i++)
                {
                    coders_info.UnPackSizes.Add(read_7z_UInt64());
                    Console.WriteLine("UnPackSizes: " + coders_info.UnPackSizes.Last());
                }
            }

            //kCRC (line 306 in 7zFormat.txt)
            flag = (Property_IDs)archive_file_reader.ReadByte();
            if (flag == Property_IDs.kCRC)
            {
                coders_info.UnPackDigests = new string[coders_info.NumFolders];
                byte[] CRCbytes;
                for (ulong i = 0; i < coders_info.NumFolders; i++)
                {
                    CRCbytes = archive_file_reader.ReadBytes(4);
                    Array.Reverse(CRCbytes);
                    coders_info.UnPackDigests[i] = Util.Bytes_to_CRC_string(CRCbytes);
                }
                flag = (Property_IDs)archive_file_reader.ReadByte();
            }
            else {
                coders_info.UnPackDigests = null;
            }

            //Check kEnd
            assert(flag == Property_IDs.kEnd,
                   "Expected kEnd (0x00), but got 0x" + Util.Byte_to_hex_string((byte)flag) + "\n" +
                   "Stream position (decimal) = " + archive_file_reader.BaseStream.Position);
            Console.WriteLine("[7z-parser] read Coders Info complete");
            return coders_info;
        }

        /// <summary>
        /// covers "Folder" section in 7zFormat.txt (line 237~276)
        /// </summary>
        private Folder read_Folder()
        {
            Console.WriteLine("[7z-parser] read Folder");
            Console.WriteLine("reader position = " + archive_file_reader.BaseStream.Position);
            Folder folder = new Folder();
            folder.NumCoders = read_7z_UInt64();
            Console.WriteLine("NumCoders = " + folder.NumCoders);

            //special byte is the byte that is documented in line 242~249 in 7zFormat.txt, stores multiple things in one single byte
            //Special thanks to Shell on sourceforge.net for helps.
            //https://web.archive.org/web/20200423074120/https://sourceforge.net/p/sevenzip/discussion/45798/thread/942ad278bc/
            byte special_byte = archive_file_reader.ReadByte();


            //CodecIdSize (line 244 in 7zFormat.txt)
            folder.CodecIdSize = (uint) special_byte & 0x0F;

            Console.WriteLine("CodecIdSize = " + folder.CodecIdSize);

            //Is Complex Coder (line 245 in 7zFormat.txt)
            folder.is_complex_coder = (special_byte & 0x10) > 0;

            //There Are Attributes (line 246 in 7zFormat.txt)
            folder.there_are_attributes = (special_byte & 0x20) > 0;

            folder.reserved_bit = (special_byte & 0x40) > 0;

            assert((special_byte & 0x80) == 0, "special_byte[0] must be 0 but it's not.");

            //CodecId (line 250 in 7zFormat.txt)
            folder.CodecId = archive_file_reader.ReadBytes((int) folder.CodecIdSize);

            //if (Is Complex Coder) (line 251~255 in 7zFormat.txt)
            if (folder.is_complex_coder)
            {
                folder.NumInStreams = read_7z_UInt64();
                folder.NumOutStreams = read_7z_UInt64();
            }

            //if (There Are Attributes) (line 256~260 in 7zFormat.txt)
            if (folder.there_are_attributes)
            {
                folder.PropertiesSize = read_7z_UInt64();
                folder.Properties = archive_file_reader.ReadBytes((int)folder.PropertiesSize);
            }

            //line 263 in 7zFormat.txt
            folder.NumBindPairs = folder.NumOutStreams - 1;

            folder.In_indexes = new ulong[folder.NumBindPairs];
            folder.Out_Indexes = new ulong[folder.NumBindPairs];

            //line 265~269 in 7zFormat.txt
            for (ulong i = 0; i < folder.NumBindPairs; i++)
            {
                folder.In_indexes[i] = read_7z_UInt64();
                folder.Out_Indexes[i] = read_7z_UInt64();
            }

            //line 271~276 in 7zFormat.txt
            folder.NumPackedStreams = folder.NumInStreams - folder.NumBindPairs;
            if (folder.NumPackedStreams > 1)
            {
                folder.indexes = new ulong[folder.NumPackedStreams];
                for (ulong i = 0; i < folder.NumPackedStreams; i++)
                {
                    folder.indexes[i] = read_7z_UInt64();
                }
            }

            Console.WriteLine("[7z-parser] read Folder complete");
            return folder;
        }

        private SubStreamsInfo read_SubStreams_info()
        {
            Console.WriteLine("[7z-parser] read Sub streams info");
            Console.WriteLine("position = 0x" + archive_file_reader.BaseStream.Position.ToString("X"));
            SubStreamsInfo sub_streams_info = new SubStreamsInfo();
            Property_IDs flag = (Property_IDs)archive_file_reader.ReadByte();

            //handles optional NumUnPackStream
            if (flag == Property_IDs.kNumUnPackStream) {
                sub_streams_info.NumUnPackStreamsInFolders = new ulong[_Additional_Streams_Info.codersInfo.NumFolders];
                for (ulong i = 0; i < _Additional_Streams_Info.codersInfo.NumFolders; i++) {
                    sub_streams_info.NumUnPackStreamsInFolders[i] = read_7z_UInt64();
                }
                flag = (Property_IDs)archive_file_reader.ReadByte();
            }

            if (flag == Property_IDs.kSize) { 

            }

            throw new ParseError("read_SubStreams_info() is not yet implemented");
        }

        /// <summary>
        /// convert 7z-UInt64 (a special format that 7-zip uses to store UInt64) to c#-UInt64 (aka REAL_UINT64)
        /// covers line 109~122 in 7zFormat.txt
        /// </summary>
        private ulong read_7z_UInt64() {
            /*
              UINT64 means real UINT64 encoded with the following scheme:

              Size of encoding sequence depends from first byte:
              First_Byte  Extra_Bytes        Value
              (binary)   
              0xxxxxxx               : ( xxxxxxx           )
              10xxxxxx    BYTE y[1]  : (  xxxxxx << (8 * 1)) + y
              110xxxxx    BYTE y[2]  : (   xxxxx << (8 * 2)) + y
              ...
              1111110x    BYTE y[6]  : (       x << (8 * 6)) + y
              11111110    BYTE y[7]  :                         y
              11111111    BYTE y[8]  :                         y

              Special thanks to Shell on sourceforge.net for helps.
              https://web.archive.org/web/20200423074120/https://sourceforge.net/p/sevenzip/discussion/45798/thread/942ad278bc/
             */
            Console.WriteLine("[tz-uint64] before: " + archive_file_reader.BaseStream.Position);
            byte first_byte = archive_file_reader.ReadByte();
            int leading1s = 0, mask = 0x80;
            for (int i = 0; i < 8; i++) {
                if ((first_byte & mask) > 0)
                {
                    leading1s++;
                    mask = mask >> 1;
                    Console.WriteLine("mask = " + mask);
                }
                else break;
            }
            ulong real_UInt64 = 0;
            for (int i = 0; i < leading1s; i++) {
                real_UInt64 += (ulong)((archive_file_reader.ReadByte()) << (i * 8));
            }
            real_UInt64 += (ulong)(first_byte & ((1 << (8 - leading1s)) - 1)) << (leading1s * 8);
            Console.WriteLine("[tz-uint64] after: " + archive_file_reader.BaseStream.Position);
            return real_UInt64;
        }

        /// <summary>
        /// If condition is false, throws ParseError with specified message.
        /// </summary>
        /// <param name="condition">condition for checking</param>
        /// <param name="message">Message to send if bad things happens</param>
        private void assert(bool condition, string message) {
            if (!condition)
                throw new ParseError(message);
        }

        /// <summary>
        /// validate CRC32 value of specified byte[], throws CRC32MismatchedError when mismatch happens, does nothing otherwise.
        /// </summary>
        /// <param name="expected_CRC32">a string that should have 8 characters exactly</param>
        private void assert_CRC32(byte[] input, string expected_CRC32) {
            string calculated_CRC32 = Crc32Algorithm.Compute(input).ToString("X").PadLeft(8, '0');
            if (!expected_CRC32.Equals(calculated_CRC32))
                throw new CRC32MismatchedError("[7z-parser] Expected CRC32 value is " + expected_CRC32 + ", but the calculated value is " + calculated_CRC32);
        }
    }

    public class ParseError : Exception
    {
        public ParseError(string message) : base(message)
        {

        }
    }

    public class CRC32MismatchedError : Exception
    {
        public CRC32MismatchedError(string message) : base(message)
        {

        }
    }

    /// <summary>
    /// covers some of "ArchiveProperties" section in 7zFormat.txt (line 194~204)
    /// </summary>
    public class ArchiveProperty {
        public Property_IDs PropertyType;
        public byte[] PropertyData;
    }

    public class StreamsInfo {
        public PackInfo packInfo;
        public CodersInfo codersInfo;
        public SubStreamsInfo subStreamsInfo;
    }

    public class PackInfo {
        public ulong PackPos, NumPackStreams;
        public ulong[] PackSizes;
        public string[] PackStreamDigests;
    }

    public class CodersInfo {
        public ulong NumFolders, DataStreamIndex;
        public Folder[] Folders;
        public bool External;
        public List<ulong> UnPackSizes;
        public string[] UnPackDigests;
}

    public class Folder {
        public ulong NumCoders, NumInStreams = 1, NumOutStreams = 1, NumBindPairs, NumPackedStreams, PropertiesSize;
        public uint CodecIdSize;
        public bool is_complex_coder, there_are_attributes, reserved_bit;
        public byte[] CodecId, Properties;
        public ulong[] In_indexes, Out_Indexes, indexes;
    }

    public class SubStreamsInfo {
        public ulong[] NumUnPackStreamsInFolders, UnPackSizes;
        public string[] Digests;
    }

    /// <summary>
    /// covers "Property IDs" section in 7zFormat.txt (line 126~165)
    /// </summary>

    public enum Property_IDs : byte
    {
        kEnd = 0x00,
        kHeader = 0x01,
        kArchiveProperties = 0x02,
        kAdditionalStreamsInfo = 0x03,
        kMainStreamsInfo = 0x04,
        kFilesInfo = 0x05,
        kPackInfo = 0x06,
        kUnPackInfo = 0x07,
        kSubStreamsInfo = 0x08,
        kSize = 0x09,
        kCRC = 0x0A,
        kFolder = 0x0B,
        kCodersUnPackSize = 0x0C,
        kNumUnPackStream = 0x0D,
        kEmptyStream = 0x0E,
        kEmptyFile = 0x0F,
        kAnti = 0x10,
        kName = 0x11,
        kCTime = 0x12,
        kATime = 0x13,
        kMTime = 0x14,
        kWinAttributes = 0x15,
        kComment = 0x16,
        kEncodedHeader = 0x17,
        kStartPos = 0x18,
        kDummy = 0x19
    }
}
