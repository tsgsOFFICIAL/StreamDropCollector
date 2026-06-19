using System.Windows.Input;

namespace UI
{
    /// <summary>
    /// Simple <see cref="ICommand"/> implementation that wraps synchronous execute and optional can-execute delegates.
    /// </summary>
    internal partial class RelayCommand(Action<object> execute, Func<object, bool>? canExecute = null) : ICommand
    {
        public bool CanExecute(object? parameter) => canExecute == null || canExecute(parameter!);

        public void Execute(object? parameter) => execute(parameter!);

        public event EventHandler? CanExecuteChanged;

        /// <summary>
        /// Raises <see cref="CanExecuteChanged"/> so bindings re-evaluate command availability.
        /// </summary>
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}