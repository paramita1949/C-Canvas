using ImageColorChanger.Services.TextEditor.Components.Notice;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Components
{
    public sealed class NoticeRuntimeServiceTests
    {
        [Fact]
        public void GetOffset_RightToLeft_Should_DecreaseOverTime()
        {
            var service = new NoticeRuntimeService();

            double x0 = service.GetOffset(0, 50, NoticeDirection.RightToLeft);
            double x1 = service.GetOffset(1000, 50, NoticeDirection.RightToLeft);

            Assert.True(x1 < x0);
        }

        [Fact]
        public void GetOffset_LeftToRight_Should_IncreaseOverTime()
        {
            var service = new NoticeRuntimeService();

            double x0 = service.GetOffset(0, 50, NoticeDirection.LeftToRight);
            double x1 = service.GetOffset(1000, 50, NoticeDirection.LeftToRight);

            Assert.True(x1 > x0);
        }

        [Fact]
        public void IsExpired_Should_RespectAutoClose()
        {
            var service = new NoticeRuntimeService();

            Assert.False(service.IsExpired(600_000, 3, autoClose: false));
            Assert.True(service.IsExpired(180_000, 3, autoClose: true));
        }

        [Fact]
        public void GetLoopingOffset_RightToLeft_Should_ReenterFromRightAfterLeavingLeft()
        {
            var service = new NoticeRuntimeService();

            // speed=100 => 130px/s。viewport=100 时 travel=100。
            // 760ms: delta≈98.8；780ms: delta≈101.4 -> 回卷到接近 0。
            double beforeWrap = service.GetLoopingOffset(760, speed: 100, NoticeDirection.RightToLeft, cycleWidth: 100);
            double afterWrap = service.GetLoopingOffset(780, speed: 100, NoticeDirection.RightToLeft, cycleWidth: 100);

            Assert.True(beforeWrap < 0);
            Assert.True(afterWrap <= 0);
            Assert.True(afterWrap > beforeWrap);
        }

        [Fact]
        public void GetLoopingOffset_LeftToRight_Should_ReenterFromLeftAfterLeavingRight()
        {
            var service = new NoticeRuntimeService();

            // speed=100 => 130px/s。viewport=100 时 travel=100。
            // 760ms: delta≈98.8；780ms: delta≈101.4 -> 回卷到接近 0。
            double beforeWrap = service.GetLoopingOffset(760, speed: 100, NoticeDirection.LeftToRight, cycleWidth: 100);
            double afterWrap = service.GetLoopingOffset(780, speed: 100, NoticeDirection.LeftToRight, cycleWidth: 100);

            Assert.True(beforeWrap > 0);
            Assert.True(afterWrap >= 0);
            Assert.True(afterWrap < beforeWrap);
        }

        [Fact]
        public void GetLoopingOffset_RightToLeft_WithShortText_Should_ReenterImmediatelyWhenTextLeavesLeft()
        {
            var service = new NoticeRuntimeService();

            // speed=100 => 130px/s。viewport=200, content=60, startX=0，travel=60。
            // 离场后应立即回卷到接近 0（不出现长时间空白）。
            double beforeLeave = service.GetLoopingOffset(450, speed: 100, NoticeDirection.RightToLeft, viewportWidth: 200, contentWidth: 60);
            double afterLeave = service.GetLoopingOffset(500, speed: 100, NoticeDirection.RightToLeft, viewportWidth: 200, contentWidth: 60);

            Assert.True(beforeLeave < 0);
            Assert.True(afterLeave <= 0);
            Assert.True(afterLeave > beforeLeave);
        }

        [Fact]
        public void GetLoopingOffset_LeftToRight_WithShortText_Should_ReenterOnlyAfterTextFullyLeavesRight()
        {
            var service = new NoticeRuntimeService();

            // speed=100 => 130px/s。viewport=200, startX=0。
            // L->R 要求：文本完全离开右侧（leftX>=200）后才回卷；即 travel=200。
            double beforeWrap = service.GetLoopingOffset(1530, speed: 100, NoticeDirection.LeftToRight, viewportWidth: 200, contentWidth: 60);
            double afterWrap = service.GetLoopingOffset(1540, speed: 100, NoticeDirection.LeftToRight, viewportWidth: 200, contentWidth: 60);

            Assert.True(beforeWrap > 190);
            Assert.True(beforeWrap < 200);
            Assert.True(afterWrap >= 0);
            Assert.True(afterWrap < 5);
        }

        [Fact]
        public void GetLoopingOffset_LeftToRight_Should_WrapAfterTextFullyExitsRightBoundary()
        {
            var service = new NoticeRuntimeService();

            // speed=100 => 130px/s。viewport=200, content=60, startX=0。
            // 要求回卷点为 leftX≈200（文本完全离开右边界）附近。
            double beforeWrap = service.GetLoopingOffset(
                elapsedMs: 1530, // delta≈198.9
                speed: 100,
                direction: NoticeDirection.LeftToRight,
                viewportWidth: 200,
                contentWidth: 60);
            double afterWrap = service.GetLoopingOffset(
                elapsedMs: 1540, // delta≈200.2
                speed: 100,
                direction: NoticeDirection.LeftToRight,
                viewportWidth: 200,
                contentWidth: 60);

            Assert.True(beforeWrap > 190);
            Assert.True(afterWrap >= 0);
            Assert.True(afterWrap < 5);
        }

        [Fact]
        public void GetLoopingOffset_LeftToRight_WithContentInset_Should_WrapAfterFullyExitingRight()
        {
            var service = new NoticeRuntimeService();

            // viewport=200, content=60, startX=20:
            // 完全离场阈值为 laneRight-startX=180。超过后应回卷到接近 0。
            double beforeLeave = service.GetLoopingOffset(
                elapsedMs: 1380, // delta≈179.4
                speed: 100,
                direction: NoticeDirection.LeftToRight,
                viewportWidth: 200,
                contentWidth: 60,
                contentStartX: 20);
            double afterLeave = service.GetLoopingOffset(
                elapsedMs: 1390, // delta≈180.7
                speed: 100,
                direction: NoticeDirection.LeftToRight,
                viewportWidth: 200,
                contentWidth: 60,
                contentStartX: 20);

            Assert.True(beforeLeave > 0);
            Assert.True(afterLeave >= 0);
            Assert.True(beforeLeave > 170);
            Assert.True(afterLeave < 5);
        }

        [Fact]
        public void GetLoopingOffset_RightToLeft_WithRightAlignedStart_Should_ReenterWithoutLongBlank()
        {
            var service = new NoticeRuntimeService();

            // 右对齐常见场景：viewport=200, content=60, startX=120，travel=startX+content=180。
            // 回卷后应立刻回到接近 0，而不是跳到很大的正值导致“消失很久”。
            double beforeLeave = service.GetLoopingOffset(
                elapsedMs: 1380, // delta≈179.4
                speed: 100,
                direction: NoticeDirection.RightToLeft,
                viewportWidth: 200,
                contentWidth: 60,
                contentStartX: 120);
            double afterLeave = service.GetLoopingOffset(
                elapsedMs: 1390, // delta≈180.7
                speed: 100,
                direction: NoticeDirection.RightToLeft,
                viewportWidth: 200,
                contentWidth: 60,
                contentStartX: 120);

            Assert.True(beforeLeave < 0);
            Assert.True(afterLeave <= 0);
            Assert.True(afterLeave > beforeLeave);
        }

        [Fact]
        public void GetLoopingOffset_LeftToRight_WithRealViewport_Should_WrapAfterFullyExitingRight()
        {
            var service = new NoticeRuntimeService();

            // 真实场景近似：viewport=1920, content=378, startX=20, speed=45 => 64px/s。
            // 完全离场阈值 laneRight-startX=1900。到达后才回卷。
            double beforeWrap = service.GetLoopingOffset(
                elapsedMs: 29687, // delta≈1899.97
                speed: 45,
                direction: NoticeDirection.LeftToRight,
                viewportWidth: 1920,
                contentWidth: 378,
                contentStartX: 20);
            double afterWrap = service.GetLoopingOffset(
                elapsedMs: 29690, // delta≈1900.16
                speed: 45,
                direction: NoticeDirection.LeftToRight,
                viewportWidth: 1920,
                contentWidth: 378,
                contentStartX: 20);

            Assert.True(beforeWrap > 0);
            Assert.True(beforeWrap > 1890);
            Assert.True(beforeWrap < 1900);
            Assert.True(afterWrap >= 0);
            Assert.True(afterWrap < 2);
        }

        [Fact]
        public void State_ManualClose_Should_BeRemembered()
        {
            var service = new NoticeRuntimeService();

            var state = service.GetStateSnapshot(100, nowMs: 1_000);
            Assert.False(state.IsManuallyClosed);

            service.MarkManuallyClosed(100);

            var stateAfter = service.GetStateSnapshot(100, nowMs: 2_000);
            Assert.True(stateAfter.IsManuallyClosed);
        }

        [Fact]
        public void Pause_And_Resume_Should_KeepElapsedProgress()
        {
            var service = new NoticeRuntimeService();

            var state = service.GetStateSnapshot(200, nowMs: 1_000);
            Assert.False(state.IsManuallyClosed);

            service.Pause(200, nowMs: 6_000);
            var paused = service.GetStateSnapshot(200, nowMs: 6_500);
            Assert.True(paused.IsManuallyClosed);
            Assert.Equal(5_000, paused.PausedElapsedMs);

            service.Resume(200, nowMs: 8_000);
            var resumed = service.GetStateSnapshot(200, nowMs: 8_000);
            Assert.False(resumed.IsManuallyClosed);
            Assert.Equal(3_000, resumed.StartTimestampMs);
        }

        [Fact]
        public void Resume_Should_ClearAutoPausedByTimeoutFlag()
        {
            var service = new NoticeRuntimeService();
            _ = service.GetStateSnapshot(300, nowMs: 1_000);
            _ = service.TryAutoPauseIfExpired(300, nowMs: 200_000, durationMinutes: 3, autoClose: true);

            service.Resume(300, nowMs: 210_000);

            var resumed = service.GetStateSnapshot(300, nowMs: 210_000);
            Assert.False(resumed.IsAutoPausedByTimeout);
            Assert.False(resumed.IsManuallyClosed);
        }

        [Fact]
        public void GetLoopingOffset_PingPong_Should_BounceBetweenLeftAndRightWalls()
        {
            var service = new NoticeRuntimeService();

            // viewport=300, content=100, startX=0 -> 可移动范围 travel=200
            double t0 = service.GetLoopingOffset(0, speed: 100, direction: NoticeDirection.PingPong, viewportWidth: 300, contentWidth: 100, contentStartX: 0);
            double t1000 = service.GetLoopingOffset(1000, speed: 100, direction: NoticeDirection.PingPong, viewportWidth: 300, contentWidth: 100, contentStartX: 0);
            double t1500 = service.GetLoopingOffset(1500, speed: 100, direction: NoticeDirection.PingPong, viewportWidth: 300, contentWidth: 100, contentStartX: 0);
            double t2000 = service.GetLoopingOffset(2000, speed: 100, direction: NoticeDirection.PingPong, viewportWidth: 300, contentWidth: 100, contentStartX: 0);
            double t3000 = service.GetLoopingOffset(3000, speed: 100, direction: NoticeDirection.PingPong, viewportWidth: 300, contentWidth: 100, contentStartX: 0);

            Assert.InRange(t0, 0, 200);
            Assert.InRange(t1000, 0, 200);
            Assert.InRange(t1500, 0, 200);
            Assert.InRange(t2000, 0, 200);
            Assert.InRange(t3000, 0, 200);

            Assert.True(t1500 > t1000); // 先向右
            Assert.True(t2000 < t1500); // 碰到右侧后回弹
            Assert.True(t3000 < t1000); // 继续接近左侧
        }

        [Fact]
        public void GetLoopingOffset_PingPong_WithContentWiderThanLane_Should_StillMove()
        {
            var service = new NoticeRuntimeService();

            // 轨道宽 200，文本宽 360。应在 [-160, 0] 内往返，而不是几乎不动。
            double t0 = service.GetLoopingOffset(0, speed: 100, direction: NoticeDirection.PingPong, viewportWidth: 200, contentWidth: 360, contentStartX: 0);
            double t1000 = service.GetLoopingOffset(1000, speed: 100, direction: NoticeDirection.PingPong, viewportWidth: 200, contentWidth: 360, contentStartX: 0);
            double t2000 = service.GetLoopingOffset(2000, speed: 100, direction: NoticeDirection.PingPong, viewportWidth: 200, contentWidth: 360, contentStartX: 0);

            Assert.InRange(t0, -160, 0);
            Assert.InRange(t1000, -160, 0);
            Assert.InRange(t2000, -160, 0);
            Assert.True(Math.Abs(t1000 - t0) > 1);
            Assert.True(Math.Abs(t2000 - t1000) > 1);
        }

        [Fact]
        public void MarkManuallyClosed_Should_UseInjectedClock()
        {
            var service = new NoticeRuntimeService(() => 9_000);
            _ = service.GetStateSnapshot(400, nowMs: 1_000);

            service.MarkManuallyClosed(400);

            var state = service.GetStateSnapshot(400, nowMs: 9_000);
            Assert.True(state.IsManuallyClosed);
            Assert.Equal(8_000, state.PausedElapsedMs);
        }

        [Fact]
        public void TryAutoPauseIfExpired_Should_TransitionOnlyOnce()
        {
            var service = new NoticeRuntimeService();
            _ = service.GetStateSnapshot(500, nowMs: 1_000);

            bool transitioned1 = service.TryAutoPauseIfExpired(500, nowMs: 190_000, durationMinutes: 3, autoClose: true);
            bool transitioned2 = service.TryAutoPauseIfExpired(500, nowMs: 200_000, durationMinutes: 3, autoClose: true);

            var state = service.GetStateSnapshot(500, nowMs: 200_000);
            Assert.True(transitioned1);
            Assert.False(transitioned2);
            Assert.True(state.IsAutoPausedByTimeout);
            Assert.True(state.PausedElapsedMs >= 189_000);
        }
    }
}
