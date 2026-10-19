namespace FocusRgb.App.Tests
{
    public sealed class WindowsSessionEndMessageTests
    {
        [Fact]
        public void Query_IsAcknowledgedWithoutBeingTreatedAsCommitted()
        {
            var committed = false;
            var handled = false;

            var result = WindowsSessionEndMessage.Handle(
                0x0011, new IntPtr(1), () => committed = true, ref handled);

            Assert.True(handled);
            Assert.Equal(new IntPtr(1), result);
            Assert.False(committed);
        }

        [Fact]
        public void CancelledEndSession_IsNotCommitted()
        {
            var committed = false;
            var handled = false;

            WindowsSessionEndMessage.Handle(
                0x0016, IntPtr.Zero, () => committed = true, ref handled);

            Assert.False(handled);
            Assert.False(committed);
        }

        [Fact]
        public void ConfirmedEndSession_IsCommitted()
        {
            var committed = false;
            var handled = false;

            WindowsSessionEndMessage.Handle(
                0x0016, new IntPtr(1), () => committed = true, ref handled);

            Assert.False(handled);
            Assert.True(committed);
        }
    }
}
