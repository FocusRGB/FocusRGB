namespace FocusRgb.App.Tests
{
    public sealed class WindowsSessionEndMessageTests
    {
        [Fact]
        public void Query_IsAcknowledgedWithoutBeingTreatedAsCommitted()
        {
            Assert.True(WindowsSessionEndMessage.IsQuery(0x0011));
            Assert.False(WindowsSessionEndMessage.IsCommitted(0x0011, new IntPtr(1)));
        }

        [Fact]
        public void CancelledEndSession_IsNotCommitted()
        {
            Assert.False(WindowsSessionEndMessage.IsCommitted(0x0016, IntPtr.Zero));
        }

        [Fact]
        public void ConfirmedEndSession_IsCommitted()
        {
            Assert.True(WindowsSessionEndMessage.IsCommitted(0x0016, new IntPtr(1)));
        }
    }
}
