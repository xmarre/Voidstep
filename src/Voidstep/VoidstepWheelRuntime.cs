using TaleWorlds.InputSystem;

namespace Voidstep
{
    internal static class VoidstepWheelRuntime
    {
        private static readonly object Sync = new object();
        private static AbilityWheelCoordinator _current;

        internal static AbilityWheelCoordinator Current
        {
            get
            {
                lock (Sync) return _current;
            }
        }

        internal static void Attach(AbilityWheelCoordinator coordinator)
        {
            lock (Sync) _current = coordinator;
        }

        internal static void Detach(AbilityWheelCoordinator coordinator)
        {
            lock (Sync)
            {
                if (ReferenceEquals(_current, coordinator))
                    _current = null;
            }
        }

        internal static bool ShouldSuppress(InputKey key)
        {
            AbilityWheelCoordinator current;
            lock (Sync) current = _current;
            return current != null && current.ShouldSuppress(key);
        }
    }
}
