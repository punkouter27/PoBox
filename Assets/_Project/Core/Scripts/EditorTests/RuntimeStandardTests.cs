using NUnit.Framework;
using PoBox.Mj;
using PoBox.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace PoBox.EditorTests
{
    /// <summary>
    /// The contracts behind the refactors of 2026-11-25: one observation layout (103 numbers, and the three a wide
    /// brain reads after them), one static reset that reaches every published singleton, and one docking rule for
    /// the screens' panels.
    /// </summary>
    public class RuntimeStandardTests
    {
        [Test]
        public void TheObservationLayoutIsPinned()
        {
            Assert.AreEqual(103, MjBoxer.ObservationSize, "the trainer's 100 numbers plus the command's 3");
            Assert.AreEqual(3, MjBoxer.BodyInputs, "envs/boxing.py's bodies: x, y, present");
            Assert.AreEqual(106, MjBoxer.WideObservationSize, "a brain that sees bodies reads the three after the 103");
        }

        [Test]
        public void EveryPublishedSingletonCanBeReset()
        {
            Assert.IsTrue(RuntimeState.CanResetAll(out string missing), $"{missing} publishes Instance but has no backing field to clear");
            Assert.DoesNotThrow(RuntimeState.ResetAll);
        }

        [Test]
        public void OneDockedPanelAtATime()
        {
            var sheet = new VisualElement { name = "sheet" };
            var debug = new VisualElement { name = "debug" };
            debug.AddToClassList("hidden");

            ScreenChrome.Show(sheet, true, sheet, debug);
            Assert.IsFalse(sheet.ClassListContains("hidden"), "the panel asked for is shown");
            Assert.IsTrue(debug.ClassListContains("hidden"), "the other stays hidden");

            ScreenChrome.Show(debug, true, sheet, debug);
            Assert.IsFalse(debug.ClassListContains("hidden"));
            Assert.IsTrue(sheet.ClassListContains("hidden"), "opening one hides the other");

            ScreenChrome.Show(debug, false, sheet, debug);
            Assert.IsTrue(debug.ClassListContains("hidden"), "closing hides only the one asked for");
            Assert.IsTrue(sheet.ClassListContains("hidden"));
        }

        [Test]
        public void HideAllReportsWhetherAnythingWasOpen()
        {
            var sheet = new VisualElement();
            var debug = new VisualElement { name = "debug" };
            sheet.AddToClassList("hidden");
            debug.AddToClassList("hidden");
            Assert.IsFalse(ScreenChrome.HideAll(sheet, debug), "nothing was open");
            sheet.RemoveFromClassList("hidden");
            Assert.IsTrue(ScreenChrome.HideAll(sheet, debug), "the sheet was open");
            Assert.IsTrue(sheet.ClassListContains("hidden"));
        }
    }
}
