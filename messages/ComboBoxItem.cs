using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace messages
{
    // retarded
    public class ComboBoxItem
    {
        public string Text;

        public ComboBoxItem(string text)
        {
            Text = text;
        }

        public override string ToString()
        {
            return Text;
        }
    }
}
