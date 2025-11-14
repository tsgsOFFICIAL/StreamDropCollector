using System.Windows.Input;

namespace UI
{
    internal partial class RelayCommand(Action<object> execute, Func<object, bool>? canExecute = null) : ICommand
    {
        public bool CanExecute(object? parameter) => canExecute == null || canExecute(parameter!);

        public void Execute(object? parameter) => execute(parameter!);

        public event EventHandler? CanExecuteChanged;
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}