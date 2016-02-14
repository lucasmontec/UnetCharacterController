using UnityEngine;
using System.Collections;

/// <summary>
/// Simple helper class to debug things to files.
/// </summary>
public class FileDebug {

    /// <summary>
    /// Log data the the specified file. This method appends data.
    /// Each call will log a new line in the file.
    /// </summary>
    /// <param name="data">The line to be writter</param>
    /// <param name="filename">The debug file to write to</param>
    public static void Log(string data, string filename = "debug") {
        using (System.IO.StreamWriter file =
                            new System.IO.StreamWriter(Application.persistentDataPath + @"\"+ filename + ".txt", true)) {
            file.WriteLine(data);
        }
    }

    /// <summary>
    /// Clears the content of the specified debug file.
    /// </summary>
    /// <param name="filename">The file to find and clear</param>
	public static void Clear(string filename = "debug") {
        string filepath = Application.persistentDataPath + @"\" + filename + ".txt";
        if (System.IO.File.Exists(filepath)) {
            System.IO.File.WriteAllText(filepath, string.Empty);
        }
    }

    /// <summary>
    /// Deletes the file log.
    /// </summary>
    /// <param name="filename"> The debug file name to delete</param>
    public static void DeleteLog(string filename = "debug") {
        string filepath = Application.persistentDataPath + @"\" + filename + ".txt";
        if (System.IO.File.Exists(filepath)) {
            System.IO.File.Delete(filepath);
        }
    }
}
