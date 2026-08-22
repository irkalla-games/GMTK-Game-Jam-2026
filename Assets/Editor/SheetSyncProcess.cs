using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Runs one of the design-sheet PowerShell scripts and pipes its output into the Unity console.
///
/// Shared by EnemySheetImporter and LevelSheetImporter so the two newer sync tools have one place to
/// shell out from. CardSheetImporter predates this and keeps its own private copy - identical in every
/// particular, but left alone rather than refactored, since it is the proven, everyday-used path and
/// touching it for a pure dedup carries risk with nothing to show for it beyond fewer lines.
/// </summary>
public static class SheetSyncProcess
{
    private const int DefaultTimeoutMs = 180_000;

    /// <summary>
    /// Runs a script relative to the repo root. Both stdout and stderr are drained asynchronously -
    /// two ReadToEnd calls deadlock as soon as one pipe's buffer fills while the other is being waited
    /// on. Returns false (and logs) on a non-zero exit, a timeout, or a script that cannot be found.
    /// </summary>
    public static bool Run(string scriptRelativePath, string arguments, int timeoutMs = DefaultTimeoutMs)
    {
        string root = Directory.GetCurrentDirectory();
        string script = Path.Combine(root, scriptRelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(script))
        {
            Debug.LogError($"Sheet sync: {scriptRelativePath} not found.");
            return false;
        }

        StringBuilder output = new();
        StringBuilder errors = new();

        System.Diagnostics.ProcessStartInfo psi = new()
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script}\" {arguments}",
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        try
        {
            using System.Diagnostics.Process process = new() { StartInfo = psi };

            process.OutputDataReceived += (_, e) => { if (e.Data != null) { output.AppendLine(e.Data); } };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) { errors.AppendLine(e.Data); } };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(); } catch { /* already gone */ }
                Debug.LogError($"Sheet sync: {scriptRelativePath} did not finish within {timeoutMs / 1000}s and was stopped.");
                return false;
            }

            string log = output.ToString().TrimEnd();
            string err = errors.ToString().TrimEnd();

            if (process.ExitCode != 0)
            {
                Debug.LogError($"Sheet sync: {scriptRelativePath} failed.\n" + (string.IsNullOrEmpty(err) ? log : err));
                return false;
            }

            if (!string.IsNullOrEmpty(log)) { Debug.Log($"[{Path.GetFileName(script)}]\n{log}"); }
            if (!string.IsNullOrEmpty(err)) { Debug.LogWarning($"[{Path.GetFileName(script)}]\n{err}"); }

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Sheet sync: could not run {scriptRelativePath} - {e.Message}. PowerShell must be on PATH.");
            return false;
        }
    }

    /// <summary>
    /// PS 5.1's Set-Content -Encoding utf8 emits a BOM on Windows. File.ReadAllText normally strips it,
    /// but trimming defensively costs nothing and JsonUtility will not tolerate one if it ever survives.
    /// </summary>
    public static string ReadJsonFile(string relativePath)
    {
        string full = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
        return File.ReadAllText(full).TrimStart('﻿', '​');
    }
}
