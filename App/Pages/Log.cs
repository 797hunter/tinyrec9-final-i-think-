// Log.cs - showing the run log (what the last run did, line by line) and jumping from a line to the step or action it is about.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TinyRec.Core;
using TinyRec.Ui;

namespace TinyRec;

public partial class MainWindow
{
    const int LogShowMax = 500;
    List<RunLog.Entry> logShown = new();
    TextBox seqLogBox;

    /// A read-only text box for log lines. Click = jump to what the line is about; double-click = also open it for editing.
    TextBox LogBox()
    {
        var box = new TextBox { IsReadOnly = true, IsReadOnlyCaretVisible = false, Cursor = Cursors.Hand };
        box.SetResourceReference(StyleProperty, "Code");
        box.FontSize = 12;
        RunLog.Entry At(MouseEventArgs e)
        {
            int ch = box.GetCharacterIndexFromPoint(e.GetPosition(box), true);
            int line = ch < 0 ? -1 : box.GetLineIndexFromCharacterIndex(ch);
            return line >= 0 && line < logShown.Count ? logShown[line] : null;
        }
        box.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2 || OnScrollBar(e)) return;
            e.Handled = true;                                            // (stops the double-click selecting a word)
            if (At(e) is { } en) LogJump(en, true);
        };
        box.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (box.SelectionLength > 0 || OnScrollBar(e)) return;       // selecting text to copy, or scrolling - not a line click
            if (At(e) is { } en) LogJump(en, false);
        };
        return box;
    }

    static bool OnScrollBar(RoutedEventArgs e)
    {
        for (var o = e.OriginalSource as DependencyObject; o != null;
             o = o is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(o) : LogicalTreeHelper.GetParent(o))
            if (o is System.Windows.Controls.Primitives.ScrollBar) return true;
        return false;
    }

    /// Put the latest lines into whichever log boxes are on screen.
    void LogRefresh(bool all2 = false)
    {
        var all = runLog?.Snapshot() ?? new List<RunLog.Entry>();
        logShown = all.Count > LogShowMax ? all.GetRange(all.Count - LogShowMax, LogShowMax) : all;
        var text = logShown.Count == 0 ? "Nothing has run yet."
                                       : string.Join("\n", logShown.Select(RunLog.Line));
        foreach (var box in new[] { edLogBox, seqLogBox })
        {
            if (box == null || (!all2 && !box.IsVisible) || box.Text == text) continue;
            box.Text = text;
            box.ScrollToEnd();
        }
    }

    void LogJump(RunLog.Entry e, bool edit)
    {
        if (runLogKind == "seq")
        {
            if (e.Step < 0 || seq == null || e.Step >= seq.Steps.Count) return;
            ShowPage("sequences");
            SeqHighlight(e.Step);
            int step = e.Step;
            Dispatcher.BeginInvoke(new Action(() => { if (step < seqRowBoxes.Count) seqRowBoxes[step].BringIntoView(); }),
                                   System.Windows.Threading.DispatcherPriority.Loaded);
            if (edit && state == "idle") SeqEdit(e.Step);
        }
        else if (e.RecTime >= 0 && rec.Events.Count > 0)
        {
            ShowPage("editor");
            viewSeg.Set("Table");
            int best = 0;                                              // the image step nearest that time
            double bestD = double.MaxValue;
            for (int i = 0; i < rec.Events.Count; i++)
            {
                double d = Math.Abs(rec.Events[i].T - e.RecTime) + (rec.Events[i].IsImage ? 0 : 0.0005);
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            EdSelectIndex(best);
        }
    }

    void LogOpenFile()
    {
        if (runLogPath == null || !File.Exists(runLogPath))
        {
            Toast(cfg.KeepLogs ? "There's no log file yet - run something first." : "Log files are switched off in Settings.", "warn");
            return;
        }
        try { Process.Start(new ProcessStartInfo(runLogPath) { UseShellExecute = true }); }
        catch (Exception e) { Toast(e.Message, "error"); }
    }
}
