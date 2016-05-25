using System;
using NFSLibrary;
using DokanNet;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Terdos.WinNFS
{
    public class NFSProxy : IDokanOperations
    {
        private NFSClient nfsClient;

        #region Debugging

        private string ToTrace(DokanFileInfo info)
        {
            var context = info.Context != null ? "<" + info.Context.GetType().Name + ">" : "<null>";

            return string.Format(CultureInfo.InvariantCulture, "{{{0}, {1}, {2}, {3}, {4}, #{5}, {6}, {7}}}",
                context, info.DeleteOnClose, info.IsDirectory, info.NoCache, info.PagingIo, info.ProcessId, info.SynchronousIo, info.WriteToEndOfFile);
        }

        private string ToTrace(DateTime? date)
        {
            return date.HasValue ? date.Value.ToString(CultureInfo.CurrentCulture) : "<null>";
        }

        private NtStatus Trace(string method, string fileName, DokanFileInfo info, NtStatus result, params string[] parameters)
        {
            var extraParameters = parameters != null && parameters.Length > 0 ? ", " + string.Join(", ", parameters) : string.Empty;

#if TRACE
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0}('{1}', {2}{3}) -> {4}",
                method, fileName, ToTrace(info), extraParameters, result));
#endif

            return result;
        }

        private NtStatus Trace(string method, string fileName, DokanFileInfo info,
                                  DokanNet.FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes,
                                  NtStatus result)
        {
#if TRACE
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0}('{1}', {2}, [{3}], [{4}], [{5}], [{6}], [{7}]) -> {8}",
                 method, fileName, ToTrace(info), access, share, mode, options, attributes, result));
#endif

            return result;
        }

        #endregion

        public NFSProxy(System.Net.IPAddress address)
        {
            nfsClient = new NFSClient(NFSClient.NFSVersion.v3);
            nfsClient.Connect(address, 1000,1000,600000);
        }

        public List<String> GetExportedDevices()
        {
            return nfsClient.GetExportedDevices();
        }

        public void Mount(String device, String path )//, DokanNet.DokanOptions d, int number)
        {
            /*
            DokanOptions dokanOptions = new DokanOptions();
            dokanOptions.DebugMode = DebugMode;
            dokanOptions.NetworkDrive = DiskOrFolder;
            dokanOptions.MountPoint = MountPoint;
            dokanOptions.UseKeepAlive = true;
            dokanOptions.UseAltStream = true;
            dokanOptions.VolumeLabel = strDriveLabel;
            dokanOptions.ThreadCount = 1;
            */

            nfsClient.MountDevice(device);
            Dokan.Mount(this, path, DokanOptions.FixedDrive | DokanOptions.DebugMode, 1);
        }

        //

        void IDokanOperations.Cleanup(string fileName, DokanFileInfo info)
        {
            Console.WriteLine("Cleanup");
            //throw new NotImplementedException();
        }

        void IDokanOperations.CloseFile(string fileName, DokanFileInfo info)
        {
            Console.WriteLine("CloseFile");
            //throw new NotImplementedException();
        }

        NtStatus IDokanOperations.CreateFile(string fileName, DokanNet.FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, DokanFileInfo info)
        {
            //fileName = "/home" + fileName.Replace('\\', '/');
            //fileName = "\\terdos\\";
            if (info.IsDirectory)
            {
                try
                {
                    switch (mode)
                    {
                        case FileMode.Open:
                            if (fileName != "\\" && nfsClient.FileExists(fileName) && !nfsClient.IsDirectory(fileName))
                            {
                                try
                                {
                                    if(nfsClient.FileExists(fileName))
                                        return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, NtStatus.Unsuccessful);
                                }
                                catch (Exception)
                                {
                                    return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.FileNotFound);
                                }
                                return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.PathNotFound);
                            }

                            //new DirectoryInfo(path).EnumerateFileSystemInfos().Any(); // you can't list the directory
                            break;

                        case FileMode.CreateNew:
                            if (nfsClient.FileExists(fileName))
                                return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.FileExists);

                            try
                            {
                                //File.GetAttributes(path).HasFlag(FileAttributes.Directory);
                                return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.AlreadyExists);
                            }
                            catch (IOException) { }

                            nfsClient.CreateDirectory(fileName);
                            break;
                    }
                }
                catch (Exception e)
                {
                    return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.AccessDenied);
                }
            }
            else
            {
                bool pathExists = true;
                bool pathIsDirectory = false;

                bool readWriteAttributes = (access & DokanNet.FileAccess.ReadData) == 0;
                bool readAccess = (access & DokanNet.FileAccess.WriteData) == 0;

                try
                {
                    pathExists = (nfsClient.FileExists(fileName) || nfsClient.IsDirectory(fileName) );
                    pathIsDirectory = nfsClient.IsDirectory(fileName);
                }
                catch (Exception e) { }

                switch (mode)
                {
                    case FileMode.Open:

                        if (pathExists)
                        {
                            if (readWriteAttributes || pathIsDirectory)
                            // check if driver only wants to read attributes, security info, or open directory
                            {
                                if (pathIsDirectory && (access & DokanNet.FileAccess.Delete) == DokanNet.FileAccess.Delete
                                    && (access & DokanNet.FileAccess.Synchronize) != DokanNet.FileAccess.Synchronize) //It is a DeleteFile request on a directory
                                    return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.AccessDenied);

                                info.IsDirectory = pathIsDirectory;
                                info.Context = new object();
                                // must set it to someting if you return DokanError.Success

                                return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.Success);
                            }
                        }
                        else
                        {
                            return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.FileNotFound);
                        }
                        break;

                    case FileMode.CreateNew:
                        if (pathExists)
                            return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.FileExists);
                        break;

                    case FileMode.Truncate:
                        if (!pathExists)
                            return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.FileNotFound);
                        break;

                    default:
                        break;
                }

                try
                {
                    nfsClient.CreateFile(fileName);
                    /*
                    info.Context = new FileStream(path, mode, readAccess ? System.IO.FileAccess.Read : System.IO.FileAccess.ReadWrite, share, 4096, options);

                    if (mode == FileMode.CreateNew
                        || mode == FileMode.Create) //Files are always created as Archive
                        attributes |= FileAttributes.Archive;
                    File.SetAttributes(path, attributes);
                    */
                }
                catch (UnauthorizedAccessException) // don't have access rights
                {
                    return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.AccessDenied);
                }
                catch (DirectoryNotFoundException)
                {
                    return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.PathNotFound);
                }
                catch (Exception ex)
                {
                    uint hr = (uint)Marshal.GetHRForException(ex);
                    switch (hr)
                    {
                        case 0x80070020: //Sharing violation
                            return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.SharingViolation);
                        default:
                            throw ex;
                    }
                }
            }
            return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.Success);
        }

        NtStatus IDokanOperations.DeleteDirectory(string fileName, DokanFileInfo info)
        {
            Console.WriteLine("DeleteDirectory");
            return NtStatus.Success;
            //throw new NotImplementedException();
        }

        NtStatus IDokanOperations.DeleteFile(string fileName, DokanFileInfo info)
        {
            Console.WriteLine("DeleteFile");
            return NtStatus.Success;
            //throw new NotImplementedException();
        }

        NtStatus IDokanOperations.FindFiles(string fileName, out IList<FileInformation> files, DokanFileInfo info)
        {
            Console.WriteLine("FindFiles");
            files = new List<FileInformation>();
            return NtStatus.Success;
            //throw new NotImplementedException();
        }

        NtStatus IDokanOperations.FindStreams(string fileName, out IList<FileInformation> streams, DokanFileInfo info)
        {
            Console.WriteLine("FindStreams");
            streams = new List<FileInformation>();
            return NtStatus.Success;
            //throw new NotImplementedException();
        }

        NtStatus IDokanOperations.FlushFileBuffers(string fileName, DokanFileInfo info)
        {
            Console.WriteLine("FlushFileBuffers");
            return NtStatus.Success;
            //throw new NotImplementedException();

        }

        NtStatus IDokanOperations.GetDiskFreeSpace(out long freeBytesAvailable, out long totalNumberOfBytes, out long totalNumberOfFreeBytes, DokanFileInfo info)
        {
            Console.WriteLine("GetDiskFreeSpace");
            freeBytesAvailable = 1024 * 1024;
            totalNumberOfBytes = 1024 * 1024;
            totalNumberOfFreeBytes = 1024 * 1024;
            return NtStatus.Success;
            //throw new NotImplementedException();
        }

        NtStatus IDokanOperations.GetFileInformation(string fileName, out FileInformation fileInfo, DokanFileInfo info)
        {
            
            Console.WriteLine("GetFileInformation");
            fileInfo = new FileInformation();
            /*
            Console.WriteLine(fileName);
            try
            {
                Console.WriteLine("one");
                if (nfsClient.FileExists(fileName))
                {
                    Console.WriteLine("two");
                    if (fileName == "\\" || nfsClient.IsDirectory(fileName))
                    {
                        FileName = fileName,
                        Attributes = finfo.Attributes,
                        CreationTime = finfo.CreationTime,
                        LastAccessTime = finfo.LastAccessTime,
                        LastWriteTime = finfo.LastWriteTime,
                        Length = (finfo is FileInfo) ? ((FileInfo)finfo).Length : 0,

                        Console.WriteLine("Is Directory");
                    }
                    else
                    {
                        Console.WriteLine("FileExists");
                    }
                }
                else
                {
                    Console.WriteLine("File Not Exists");
                }*/
                /*
                FileSystemInfo finfo = new FileInfo(path);
                if (!finfo.Exists)
                    finfo = new DirectoryInfo(path);

                fileInfo = new FileInformation
                {
                    FileName = fileName,
                    Attributes = finfo.Attributes,
                    CreationTime = finfo.CreationTime,
                    LastAccessTime = finfo.LastAccessTime,
                    LastWriteTime = finfo.LastWriteTime,
                    Length = (finfo is FileInfo) ? ((FileInfo)finfo).Length : 0,
                };
                return Trace("GetFileInformation", fileName, info, DokanResult.Success);
                */
           /* }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }*/
            return NtStatus.Success;
            //throw new NotImplementedException();

        }

        NtStatus IDokanOperations.GetFileSecurity(string fileName, out FileSystemSecurity security, AccessControlSections sections, DokanFileInfo info)
        {
            Console.WriteLine("GetFileSecurity");
            security = null;
            return NtStatus.Success;
            //throw new NotImplementedException();
        }

        NtStatus IDokanOperations.GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features, out string fileSystemName, DokanFileInfo info)
        {
            Console.WriteLine("GetVolumeInformation");
            volumeLabel = "cheese";
            features = FileSystemFeatures.ReadOnlyVolume;
            fileSystemName = "FileSystem";
            return NtStatus.Success;
            //throw new NotImplementedException();

        }

        NtStatus IDokanOperations.LockFile(string fileName, long offset, long length, DokanFileInfo info)
        {
            Console.WriteLine("LockFile");
            return NtStatus.Success;
            //throw new NotImplementedException();

        }

        NtStatus IDokanOperations.Mounted(DokanFileInfo info)
        {
            Console.WriteLine("Mounted");
            Console.WriteLine(info.Context);
            Console.WriteLine(info.DeleteOnClose);
            Console.WriteLine(info.IsDirectory);
            Console.WriteLine(info.NoCache);
            Console.WriteLine(info.PagingIo);
            Console.WriteLine(info.ProcessId);
            Console.WriteLine(info.SynchronousIo);
            Console.WriteLine(info.WriteToEndOfFile);
            

            return NtStatus.Success;
            //throw new NotImplementedException();
        }

        NtStatus IDokanOperations.MoveFile(string oldName, string newName, bool replace, DokanFileInfo info)
        {
            Console.WriteLine("MoveFile");
            return NtStatus.Success;
            //throw new NotImplementedException();

        }

        NtStatus IDokanOperations.ReadFile(string fileName, byte[] buffer, out int bytesRead, long offset, DokanFileInfo info)
        {
            Console.WriteLine("ReadFile");
            bytesRead = 0;
            return NtStatus.Success;
            //throw new NotImplementedException();
        }

        NtStatus IDokanOperations.SetAllocationSize(string fileName, long length, DokanFileInfo info)
        {
            Console.WriteLine("SetAllocationSize");
            return NtStatus.Success;
            //throw new NotImplementedException();
        }

        NtStatus IDokanOperations.SetEndOfFile(string fileName, long length, DokanFileInfo info)
        {
            Console.WriteLine("SetEndOfFile");
            return NtStatus.Success;
            //throw new NotImplementedException();
        }

        NtStatus IDokanOperations.SetFileAttributes(string fileName, FileAttributes attributes, DokanFileInfo info)
        {
            Console.WriteLine("SetFileAttributes");
            return NtStatus.Success;
            //throw new NotImplementedException();
        }

        NtStatus IDokanOperations.SetFileSecurity(string fileName, FileSystemSecurity security, AccessControlSections sections, DokanFileInfo info)
        {
            Console.WriteLine("SetFileSecurity");
            return NtStatus.Success;
            //throw new NotImplementedException();
        }

        NtStatus IDokanOperations.SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime, DateTime? lastWriteTime, DokanFileInfo info)
        {
            Console.WriteLine("SetFileTime");
            return NtStatus.Success;
            //throw new NotImplementedException();
        }

        NtStatus IDokanOperations.UnlockFile(string fileName, long offset, long length, DokanFileInfo info)
        {
            Console.WriteLine("UnlockFile");
            return NtStatus.Success;
            //throw new NotImplementedException();
        }

        NtStatus IDokanOperations.Unmounted(DokanFileInfo info)
        {
            Console.WriteLine("Unmounted");
            return NtStatus.Success;
            //throw new NotImplementedException();
        }

        NtStatus IDokanOperations.WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset, DokanFileInfo info)
        {
            Console.WriteLine("WriteFile");
            bytesWritten = 0;
            return NtStatus.Success;
            //throw new NotImplementedException();
        }
    }
}
