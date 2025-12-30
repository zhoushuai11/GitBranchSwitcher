using System;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Diagnostics;

namespace GitBranchSwitcher
{
    public class NetworkShare
    {
        // 返回错误代码，0 表示成功
        public static int Connect(string remoteUri, string username, string password)
        {
            var netResource = new NetResource
            {
                Scope = ResourceScope.GlobalNetwork,
                ResourceType = ResourceType.Disk,
                DisplayType = ResourceDisplaytype.Share,
                RemoteName = remoteUri
            };

            // 尝试连接
            int result = WNetAddConnection2(netResource, password, username, 0);

            // 错误 1219: 提供的凭据与现有连接集冲突
            // 错误 85:  本地设备名已在使用中 (通常指已经连接上了)
            if (result == 1219 || result == 85)
            {
                // 先断开旧连接
                WNetCancelConnection2(remoteUri, 0, true);
                // 再重试
                result = WNetAddConnection2(netResource, password, username, 0);
            }

            return result;
        }

        [DllImport("mpr.dll")]
        private static extern int WNetAddConnection2(NetResource netResource, string password, string username, int flags);

        [DllImport("mpr.dll")]
        private static extern int WNetCancelConnection2(string name, int flags, bool force);

        [StructLayout(LayoutKind.Sequential)]
        public class NetResource
        {
            public ResourceScope Scope;
            public ResourceType ResourceType;
            public ResourceDisplaytype DisplayType;
            public int Usage;
            public string LocalName;
            public string RemoteName;
            public string Comment;
            public string Provider;
        }

        public enum ResourceScope : int { Connected = 1, GlobalNetwork, Remembered, Recent, Context }
        public enum ResourceType : int { Any = 0, Disk = 1, Print = 2, Reserved = 8 }
        public enum ResourceDisplaytype : int { Generic = 0, Domain, Server, Share, File, Group, Network, Root, Shareadmin, Directory, Tree, Ndscontainer }
    }
}