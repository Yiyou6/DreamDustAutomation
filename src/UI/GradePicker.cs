using System;

namespace DreamDustAutomation.UI
{
    // Compact two-column picker using the game's own keyboard/mouse radio group.
    // It contains no storage nodes, so changing this layout cannot move saved items.
    internal sealed class GradePicker : MachineryStack
    {
        private readonly MachineryRadioGroup group = new MachineryRadioGroup();
        private readonly MachineryRadioButton[] buttons;

        public GradePicker(string[] captions, Action<int> changed, int columns = 2) : base(false, 8)
        {
            buttons = new MachineryRadioButton[captions.Length];
            var stacks = new MachineryStack[columns];
            for (int i = 0; i < columns; i++) stacks[i] = new MachineryStack(true, 3).SetAlign(Align.Start);
            for (int i = 0; i < captions.Length; i++)
            {
                buttons[i] = new MachineryRadioButton(captions[i], i == 0);
                group.Add(buttons[i]);
                stacks[i % columns].Add(buttons[i]);
            }
            foreach (var stack in stacks) Add(stack);
            group.onChanged = changed;
        }

        public MachineryRadioButton Get(int index) => buttons[index];
        public void Select(int index) => group.Select(index, false);
    }
}
