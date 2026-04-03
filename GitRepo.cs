namespace GitBranchSwitcher {
    public enum RepoSwitchSeverity {
        None,
        Warning,
        Error
    }

    public class GitRepo {
        public string Name { get; set; }
        public string Path { get; set; }
        public string CurrentBranch { get; set; } = "(unknown)";
        public bool SwitchOk { get; set; }
        public string LastMessage { get; set; }
        public RepoSwitchSeverity SwitchSeverity { get; set; } = RepoSwitchSeverity.None;
        public string SwitchStatusText { get; set; } = "";
        public bool IsSwitchQueued { get; set; } = false;
        public bool IsSwitching { get; set; } = false;
        public System.DateTime? SwitchStartedAt { get; set; }
        public string LiveStatus { get; set; } = "";

        public int Incoming { get; set; } = 0;
        public int Outgoing { get; set; } = 0;
        public bool HasUpstream { get; set; } = true;
        public bool IsSyncChecked { get; set; } = false;
        public bool IsDirty { get; set; } = false;

        public GitRepo(string name, string path) {
            Name = name;
            Path = path;
        }
    }

    public class FileChangeItem {
        public string Status { get; set; } = "";
        public string FilePath { get; set; } = "";
    }
}
