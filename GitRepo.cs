namespace GitBranchSwitcher
{
    public class GitRepo
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string? CurrentBranch { get; set; }
        public bool SwitchOk { get; set; }
        public string? LastMessage { get; set; }

        public GitRepo(string name, string path) { Name = name; Path = path; }
    }
}