using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

// The same minus/value/plus design as StudioNumber, with decimal seconds.
sealed class StudioDecimalNumber : UserControl
{
    readonly decimal minimum, maximum, increment;
    readonly TextBox input;
    readonly ToolTip tips = new ToolTip();
    decimal value;
    public event EventHandler ValueChanged;

    internal StudioDecimalNumber(decimal min, decimal max, decimal step, string field)
    {
        minimum = min; maximum = max; increment = step; value = min;
        BackColor = StudioTheme.Rail;
        var parts = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            Margin = Padding.Empty, Padding = new Padding(1), BackColor = StudioTheme.Rail };
        parts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 27));
        parts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        parts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 27));
        parts.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(parts);
        input = new TextBox {
            Text = Format(value), BackColor = StudioTheme.Rail, ForeColor = StudioTheme.Text,
            Font = StudioTheme.Font(10, true), TextAlign = HorizontalAlignment.Center,
            BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0),
            AccessibleName = field + " in seconds"
        };
        input.KeyPress += delegate(object sender, KeyPressEventArgs e) {
            e.Handled = !char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) &&
                e.KeyChar != '.' && e.KeyChar != ',' &&
                !CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator.Contains(e.KeyChar.ToString());
        };
        input.KeyDown += delegate(object sender, KeyEventArgs e) {
            if (e.KeyCode != Keys.Up && e.KeyCode != Keys.Down) return;
            Value += e.KeyCode == Keys.Up ? increment : -increment;
            e.SuppressKeyPress = true;
        };
        input.TextChanged += delegate {
            decimal entered;
            if (TryRead(out entered) && entered >= minimum && entered <= maximum) Set(entered);
        };
        input.Leave += delegate { Value = Value; };
        parts.Controls.Add(Arrow("−", -increment, field), 0, 0);
        parts.Controls.Add(input, 1, 0);
        parts.Controls.Add(Arrow("+", increment, field), 2, 0);
    }

    static string Format(decimal number) { return number.ToString("0.00", CultureInfo.CurrentCulture); }
    bool TryRead(out decimal number)
    {
        const NumberStyles style = NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign;
        return decimal.TryParse(input.Text, style, CultureInfo.CurrentCulture, out number) ||
            decimal.TryParse(input.Text, style, CultureInfo.InvariantCulture, out number);
    }
    Button Arrow(string caption, decimal change, string field)
    {
        var button = StudioTheme.Button(caption, repeat: true);
        button.Dock = DockStyle.Fill; button.Padding = Padding.Empty;
        button.MinimumSize = Size.Empty; button.Margin = new Padding(1);
        button.Font = StudioTheme.Font(10, true);
        button.AccessibleName = (change < 0 ? "Decrease " : "Increase ") + field;
        tips.SetToolTip(button, button.AccessibleName);
        button.Click += delegate { Value += change; };
        return button;
    }
    public decimal Value
    {
        get { decimal entered; return TryRead(out entered) ? Math.Max(minimum, Math.Min(maximum, entered)) : value; }
        set {
            decimal next = Math.Round(Math.Max(minimum, Math.Min(maximum, value)), 2, MidpointRounding.AwayFromZero);
            string text = Format(next);
            if (input.Text != text) input.Text = text;
            Set(next);
        }
    }
    void Set(decimal next)
    {
        if (value == next) return;
        value = next;
        var handler = ValueChanged;
        if (handler != null) handler(this, EventArgs.Empty);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) tips.Dispose();
        base.Dispose(disposing);
    }
}
