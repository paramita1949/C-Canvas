using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// WPF 文件夹选择器助手（使用 Windows Vista+ 新样式对话框）
    /// 替代 System.Windows.Forms.FolderBrowserDialog
    /// </summary>
    public static class WpfFolderBrowserHelper
    {
        /// <summary>
        /// 显示文件夹选择对话框
        /// </summary>
        /// <param name="title">对话框标题</param>
        /// <param name="initialDirectory">初始目录</param>
        /// <param name="owner">所有者窗口（用于居中显示）</param>
        /// <returns>选中的路径，如果取消则返回 null</returns>
        public static string SelectFolder(string title = "选择文件夹", string initialDirectory = null, Window owner = null)
        {
            try
            {
                // 使用 Windows Vista+ 文件对话框（IFileDialog）
                var dialog = new NativeFolderBrowserDialog();
                dialog.Title = title;
                
                if (!string.IsNullOrEmpty(initialDirectory))
                {
                    dialog.InitialDirectory = initialDirectory;
                }
                
                // 获取所有者窗口句柄
                IntPtr hwndOwner = IntPtr.Zero;
                if (owner != null)
                {
                    var helper = new System.Windows.Interop.WindowInteropHelper(owner);
                    hwndOwner = helper.Handle;
                }
                
                bool? result = dialog.ShowDialog(hwndOwner);
                
                if (result == true)
                {
                    return dialog.FileName;
                }
                
                return null;
            }
            catch
            {
                // 如果新对话框失败，降级使用旧对话框
                return ShowFallbackDialog(title, initialDirectory);
            }
        }
        
        /// <summary>
        /// 降级方案：使用 Windows Forms FolderBrowserDialog
        /// </summary>
        private static string ShowFallbackDialog(string title, string initialDirectory)
        {
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = title,
                ShowNewFolderButton = true
            };
            
            if (!string.IsNullOrEmpty(initialDirectory) && System.IO.Directory.Exists(initialDirectory))
            {
                folderDialog.SelectedPath = initialDirectory;
            }
            
            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                return folderDialog.SelectedPath;
            }
            
            return null;
        }
    }
    
    /// <summary>
    /// Windows Vista+ 样式的文件夹选择对话框（使用 IFileDialog）
    /// </summary>
    internal class NativeFolderBrowserDialog
    {
        public string Title { get; set; }
        public string InitialDirectory { get; set; }
        public string FileName { get; private set; }
        
        public bool? ShowDialog(IntPtr owner)
        {
            IFileDialog dialog = null;
            try
            {
                // 创建 FileOpenDialog
                dialog = (IFileDialog)new FileOpenDialog();
                
                // 设置选项：选择文件夹
                dialog.GetOptions(out uint options);
                options |= FOS.FOS_PICKFOLDERS | FOS.FOS_FORCEFILESYSTEM | FOS.FOS_PATHMUSTEXIST;
                dialog.SetOptions(options);
                
                // 设置标题
                if (!string.IsNullOrEmpty(Title))
                {
                    dialog.SetTitle(Title);
                }
                
                // 设置初始目录
                if (!string.IsNullOrEmpty(InitialDirectory) && System.IO.Directory.Exists(InitialDirectory))
                {
                    IShellItem item;
                    if (SHCreateItemFromParsingName(InitialDirectory, IntPtr.Zero, typeof(IShellItem).GUID, out item) == 0)
                    {
                        dialog.SetFolder(item);
                        Marshal.ReleaseComObject(item);
                    }
                }
                
                // 显示对话框
                int hr = dialog.Show(owner);
                
                // 用户点击确定
                if (hr == 0)
                {
                    dialog.GetResult(out IShellItem resultItem);
                    resultItem.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out string path);
                    FileName = path;
                    Marshal.ReleaseComObject(resultItem);
                    return true;
                }
                
                // 用户取消
                return false;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (dialog != null)
                {
                    Marshal.ReleaseComObject(dialog);
                }
            }
        }
        
        #region COM Interop
        
        [ComImport]
        [Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        [ClassInterface(ClassInterfaceType.None)]
        private class FileOpenDialog { }
        
        [ComImport]
        [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr hwndOwner);
            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, int fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
        }
        
        [ComImport]
        [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid bhid, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }
        
        private enum SIGDN : uint
        {
            SIGDN_FILESYSPATH = 0x80058000
        }
        
        private static class FOS
        {
            public const uint FOS_PICKFOLDERS = 0x00000020;
            public const uint FOS_FORCEFILESYSTEM = 0x00000040;
            public const uint FOS_PATHMUSTEXIST = 0x00000800;
        }
        
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            out IShellItem ppv);
        
        #endregion
    }
}

