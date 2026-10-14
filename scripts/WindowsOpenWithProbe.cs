using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CeciliaLibrary.Diagnostics
{
    [ComImport, Guid("F04061AC-1659-4A3F-A954-775AA57FC083"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAssocHandler
    {
        [PreserveSig] int GetName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int GetUIName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] out string path, out int index);
        [PreserveSig] int IsRecommended();
    }
    [ComImport, Guid("973810AE-9599-4B88-9E4D-6EE98C9552DA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IEnumAssocHandlers
    {
        [PreserveSig] int Next(uint count, [MarshalAs(UnmanagedType.Interface)] out IAssocHandler handler, out uint fetched);
    }
    public sealed class Handler
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public bool Recommended { get; set; }
        public int NameResult { get; set; }
        public int DisplayNameResult { get; set; }
    }
    public static class OpenWithProbe
    {
        [DllImport("shell32.dll", CharSet=CharSet.Unicode)]
        private static extern int SHAssocEnumHandlers(string extension, uint filter, out IEnumAssocHandlers handlers);

        public static Handler[] Enumerate(string extension, bool recommendedOnly)
        {
            IEnumAssocHandlers enumerator;
            Marshal.ThrowExceptionForHR(SHAssocEnumHandlers(extension, recommendedOnly ? 1u : 0u, out enumerator));
            var results = new List<Handler>();
            if (enumerator == null) return results.ToArray();
            try
            {
                while (true)
                {
                    IAssocHandler handler;
                    uint fetched;
                    int status = enumerator.Next(1, out handler, out fetched);
                    Marshal.ThrowExceptionForHR(status);
                    if (fetched == 0 || handler == null) break;
                    try
                    {
                        string name, displayName;
                        int nameResult=handler.GetName(out name);
                        int displayResult=handler.GetUIName(out displayName);
                        results.Add(new Handler { Name=name, DisplayName=displayName, Recommended=handler.IsRecommended()==0, NameResult=nameResult, DisplayNameResult=displayResult });
                    }
                    finally { Marshal.ReleaseComObject(handler); }
                }
            }
            finally { Marshal.ReleaseComObject(enumerator); }
            return results.ToArray();
        }
    }
}
