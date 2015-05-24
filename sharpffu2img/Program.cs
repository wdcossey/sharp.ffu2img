/* 
 MIT License, Copyright 2015 wdcossey
 Full text in 'LICENSE' file

 May not work for any FFU files other than the Raspberry Pi 2 Windows 10 Insider Preview image.
 Tested on the 2015-05-12 release image
 Use at your own risk, and let me know if it fails for your situation.

 This is a C# port of the original Python project by: t0x0
 https://github.com/t0x0/random/blob/master/ffu2img.py
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace Sharp.ffu2img
{
  internal static class Program
  {
    // ReSharper disable InconsistentNaming
    // ReSharper disable UnusedAutoPropertyAccessor.Local
    private struct SecurityHeader
    {
      public uint cbSize { get; set; }
      public string signature { get; set; }
      public uint dwChunkSizeInKb { get; set; }
      public uint dwAlgId { get; set; }
      public uint dwCatalogSize { get; set; }
      public uint dwHashTableSize { get; set; }
    }

    private struct ImageHeader
    {
      public uint cbSize { get; set; }
      public string signature { get; set; }
      public uint ManifestLength { get; set; }
      public uint dwChunkSize { get; set; }
    }

    private struct StoreHeader
    {
      public uint dwUpdateType { get; set; }
      public ushort MajorVersion { get; set; }
      public ushort MinorVersion { get; set; }
      public ushort FullFlashMajorVersion { get; set; }
      public ushort FullFlashMinorVersion { get; set; }
      public string szPlatformId { get; set; }
      public uint dwBlockSizeInBytes { get; set; }
      public uint dwWriteDescriptorCount { get; set; }
      public uint dwWriteDescriptorLength { get; set; }
      public uint dwValidateDescriptorCount { get; set; }
      public uint dwValidateDescriptorLength { get; set; }
      public uint dwInitialTableIndex { get; set; }
      public uint dwInitialTableCount { get; set; }
      public uint dwFlashOnlyTableIndex { get; set; }
      public uint dwFlashOnlyTableCount { get; set; }
      public uint dwFinalTableIndex { get; set; }
      public uint dwFinalTableCount { get; set; }
    }

    private struct BlockDataEntry
    {
      public uint dwDiskAccessMethod { get; set; }
      public uint dwBlockIndex { get; set; }
      public uint dwLocationCount { get; set; }
      public uint dwBlockCount { get; set; }
    }
    // ReSharper restore InconsistentNaming
    // ReSharper restore UnusedAutoPropertyAccessor.Local

    [STAThread]
    private static void Main(string[] argsInput)
    {
      try
      {

        var args = new List<string>(argsInput);

        if (args.Count < 1)
        {
          if (!Environment.UserInteractive)
            throw new Exception(
              "Error: no filenames provided. Usage: sharpffu2img input.ffu [output.img]\nWarning, will overwrite output file without prior permission.");

          using (var openDialog = new OpenFileDialog())
          {
            openDialog.CheckFileExists = true;
            openDialog.Filter = "Flash.ffu|Flash.ffu|All .ffu File(s)|*.ffu";

            if (openDialog.ShowDialog() != DialogResult.OK || !File.Exists(openDialog.FileName))
              return;

            args.Add(openDialog.FileName);
          }

          using (var saveDialog = new SaveFileDialog())
          {
            saveDialog.CheckFileExists = false;
            saveDialog.Filter = "Raw .img File(s)|*.img";
            saveDialog.FileName = Path.ChangeExtension(args[0], ".img");

            if (saveDialog.ShowDialog() != DialogResult.OK)
              return;

            args.Add(saveDialog.FileName);
          }

        }

        var ffupath = args[0];

        var imgpath = args.Count == 2 ? args[1] : Path.ChangeExtension(args[0], ".img");

        Console.WriteLine("Input File: {0}", ffupath);
        Console.WriteLine("Output File: {0}", imgpath);

        if (File.Exists(imgpath))
          File.Delete(imgpath);

        using (var ffufp = new BinaryReader(File.OpenRead(ffupath)))
        using (var imgfp = new BinaryWriter(File.OpenWrite(imgpath)))
        using (
          var logfp = new StreamWriter(File.Open("sharpffu2img.log", FileMode.Append, FileAccess.Write),
            Encoding.Default))
        {
          var ffuSecHeader = ReadSecurityHeader(ffufp, logfp);

          ReadImageHeader(ffufp, ffuSecHeader, logfp);

          var ffuStoreHeader = ReadStoreHeader(ffufp, logfp);

          Console.WriteLine("Block data entries begin: {0:x8}", ffufp.BaseStream.Position);
          logfp.WriteLine("Block data entries begin: {0:x8}", ffufp.BaseStream.Position);

          Console.WriteLine("Block data entries end: {0:x8}",
            ffufp.BaseStream.Position + ffuStoreHeader.dwWriteDescriptorLength);
          logfp.WriteLine("Block data entries end: {0:x8}",
            ffufp.BaseStream.Position + ffuStoreHeader.dwWriteDescriptorLength);

          var blockdataaddress = ffufp.BaseStream.Position + ffuStoreHeader.dwWriteDescriptorLength;
          blockdataaddress = blockdataaddress + (ffuSecHeader.dwChunkSizeInKb*1024) -
                             (blockdataaddress%((int) (ffuSecHeader.dwChunkSizeInKb*1024)));

          logfp.WriteLine("Block data chunks begin: {0:x8}", blockdataaddress);
          Console.WriteLine("Block data chunks begin: {0:x8}", blockdataaddress);

          var iBlock = 0u;
          var oldblockcount = 0u;
          while (iBlock < ffuStoreHeader.dwWriteDescriptorCount)
          {
            Console.Write("\r{0} blocks, {1}KB written", iBlock, (iBlock*ffuStoreHeader.dwBlockSizeInBytes)/1024);
            logfp.WriteLine("Block data entry from: {0:x8}", ffufp.BaseStream.Position);

            var currentBlockDataEntry = ReadBlockDataEntry(ffufp);

            if (Math.Abs(currentBlockDataEntry.dwBlockCount - oldblockcount) > 1)
              Console.Write("\r{0} blocks, {1}KB written - Delay expected. Please wait.", iBlock,
                (iBlock*ffuStoreHeader.dwBlockSizeInBytes)/1024);

            oldblockcount = currentBlockDataEntry.dwBlockCount;

            LogPropertyValues(currentBlockDataEntry, logfp);

            var curraddress = ffufp.BaseStream.Position;

            ffufp.BaseStream.Seek(blockdataaddress + (iBlock*ffuStoreHeader.dwBlockSizeInBytes), SeekOrigin.Begin);
            imgfp.BaseStream.Seek(
              (Convert.ToInt64(currentBlockDataEntry.dwBlockCount)*Convert.ToInt64(ffuStoreHeader.dwBlockSizeInBytes)),
              SeekOrigin.Begin);
            imgfp.Write(ffufp.ReadBytes((int) ffuStoreHeader.dwBlockSizeInBytes));

            ffufp.BaseStream.Seek(curraddress, SeekOrigin.Begin);

            iBlock = iBlock + 1;
          }

          Console.Write("\nWrite complete.");
        }

      }
      catch (Exception ex)
      {
        Console.WriteLine(ex.Message);
      }

      Console.Read();
    }

    private static SecurityHeader ReadSecurityHeader(BinaryReader reader, StreamWriter logWriter)
    {
      logWriter.WriteLine("FFUSecHeader begin: {0:x8}", reader.BaseStream.Position);

      var result = new SecurityHeader
      {
        cbSize = reader.ReadUInt32(),
        signature = new string(reader.ReadChars(12)),
        dwChunkSizeInKb = reader.ReadUInt32(),
        dwAlgId = reader.ReadUInt32(),
        dwCatalogSize = reader.ReadUInt32(),
        dwHashTableSize = reader.ReadUInt32(),
      };

      if (!result.signature.Equals("SignedImage "))
      {
        logWriter.WriteLine("Exiting, incorrect signature: {0}", result.signature);
        throw new Exception(string.Format("Error: security header signature incorrect: {0}", result.signature));
      }

      LogPropertyValues(result, logWriter);

      reader.BaseStream.Seek(result.dwCatalogSize, SeekOrigin.Current);
      reader.BaseStream.Seek(result.dwHashTableSize, SeekOrigin.Current);
      GoToEndOfChunk(reader, (int)result.dwChunkSizeInKb);

      return result;
    }

    private static void ReadImageHeader(BinaryReader reader, SecurityHeader securityHeader,
      StreamWriter logWriter)
    {
      logWriter.WriteLine("FFUImgHeader begin: {0:x8}", reader.BaseStream.Position);

      var result = new ImageHeader
      {
        cbSize = reader.ReadUInt32(),
        signature = new string(reader.ReadChars(12)),
        ManifestLength = reader.ReadUInt32(),
        dwChunkSize = reader.ReadUInt32(),
      };

      if (!result.signature.Equals("ImageFlash  "))
      {
        logWriter.WriteLine("Exiting, incorrect signature: {0}", result.signature);
        throw new Exception(string.Format("Error: image header signature incorrect. {0}", result.signature));
      }

      LogPropertyValues(result, logWriter);

      reader.BaseStream.Seek(result.ManifestLength, SeekOrigin.Current);

      GoToEndOfChunk(reader, (int)securityHeader.dwChunkSizeInKb);
    }

    private static StoreHeader ReadStoreHeader(BinaryReader reader, StreamWriter logWriter)
    {
      logWriter.WriteLine("FFUStoreHeader begin: {0:x8}", reader.BaseStream.Position);

      var result = new StoreHeader
      {
        dwUpdateType = reader.ReadUInt32(),
        MajorVersion = reader.ReadUInt16(),
        MinorVersion = reader.ReadUInt16(),
        FullFlashMajorVersion = reader.ReadUInt16(),
        FullFlashMinorVersion = reader.ReadUInt16(),
        szPlatformId = new string(reader.ReadChars(192)),
        dwBlockSizeInBytes = reader.ReadUInt32(),
        dwWriteDescriptorCount = reader.ReadUInt32(),
        dwWriteDescriptorLength = reader.ReadUInt32(),
        dwValidateDescriptorCount = reader.ReadUInt32(),
        dwValidateDescriptorLength = reader.ReadUInt32(),
        dwInitialTableIndex = reader.ReadUInt32(),
        dwInitialTableCount = reader.ReadUInt32(),
        dwFlashOnlyTableIndex = reader.ReadUInt32(),
        dwFlashOnlyTableCount = reader.ReadUInt32(),
        dwFinalTableIndex = reader.ReadUInt32(),
        dwFinalTableCount = reader.ReadUInt32(),
      };

      LogPropertyValues(result, logWriter);
      
      reader.BaseStream.Seek(result.dwValidateDescriptorLength, SeekOrigin.Current);

      return result;
    }
    private static BlockDataEntry ReadBlockDataEntry(BinaryReader reader)
    {
      var result = new BlockDataEntry
      {
        dwDiskAccessMethod = reader.ReadUInt32(),
        dwBlockIndex = reader.ReadUInt32(),
        dwLocationCount = reader.ReadUInt32(),
        dwBlockCount = reader.ReadUInt32()
      };

      return result;
    }

    private static void GoToEndOfChunk(BinaryReader reader, Int32 chunkSizeinKb)
    {
      var remainderOfChunk = reader.BaseStream.Position%(chunkSizeinKb*1024);
      var distanceToChunkEnd = (chunkSizeinKb*1024) - remainderOfChunk;
      reader.BaseStream.Seek(distanceToChunkEnd, SeekOrigin.Current);
    }

    private static void LogPropertyValues<T>(T obj, StreamWriter logWriter) where T : struct
    {
      if (logWriter == null) 
        return;

      foreach (var property in obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
      {
        logWriter.WriteLine("{0} = {1}", property.Name, property.GetValue(obj, null));
      }
    }
  }
}
