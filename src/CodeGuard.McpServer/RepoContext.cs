namespace CodeGuard.McpServer;

/// <param name="Root">Repository root. Every tool is sandboxed to this folder.</param>
/// <param name="AllowExec">Whether build/test tools may run. Off by default: running PR code is dangerous.</param>
public sealed record RepoContext(string Root, bool AllowExec);
