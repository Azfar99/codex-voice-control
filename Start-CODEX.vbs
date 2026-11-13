Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
shell.CurrentDirectory = fso.GetParentFolderName(WScript.ScriptFullName)
shell.Run Chr(34) & shell.CurrentDirectory & "\CODEX.exe" & Chr(34), 1, False
