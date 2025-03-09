// File: FrameControl.cs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VSPilot.UI.Windows
{
    public class FrameControl : Control
    {
        // Register dependency property for ShouldShowBusySpinner
        public static readonly DependencyProperty ShouldShowBusySpinnerProperty =
            DependencyProperty.Register(
                nameof(ShouldShowBusySpinner),
                typeof(bool),
                typeof(FrameControl),
                new PropertyMetadata(false));

        public bool ShouldShowBusySpinner
        {
            get => (bool)GetValue(ShouldShowBusySpinnerProperty);
            set => SetValue(ShouldShowBusySpinnerProperty, value);
        }

        // Register dependency property for ProfileCardButtonClickCommand
        public static readonly DependencyProperty ProfileCardButtonClickCommandProperty =
            DependencyProperty.Register(
                nameof(ProfileCardButtonClickCommand),
                typeof(ICommand),
                typeof(FrameControl),
                new PropertyMetadata(null));

        public ICommand ProfileCardButtonClickCommand
        {
            get => (ICommand)GetValue(ProfileCardButtonClickCommandProperty);
            set => SetValue(ProfileCardButtonClickCommandProperty, value);
        }

        // Register dependency property for ShouldShowSignIn
        public static readonly DependencyProperty ShouldShowSignInProperty =
            DependencyProperty.Register(
                nameof(ShouldShowSignIn),
                typeof(bool),
                typeof(FrameControl),
                new PropertyMetadata(false));

        public bool ShouldShowSignIn
        {
            get => (bool)GetValue(ShouldShowSignInProperty);
            set => SetValue(ShouldShowSignInProperty, value);
        }

        // Register dependency property for ShouldShowUserImage
        public static readonly DependencyProperty ShouldShowUserImageProperty =
            DependencyProperty.Register(
                nameof(ShouldShowUserImage),
                typeof(bool),
                typeof(FrameControl),
                new PropertyMetadata(false));

        public bool ShouldShowUserImage
        {
            get => (bool)GetValue(ShouldShowUserImageProperty);
            set => SetValue(ShouldShowUserImageProperty, value);
        }

        // Register dependency property for UserImage (of type ImageSource)
        public static readonly DependencyProperty UserImageProperty =
            DependencyProperty.Register(
                nameof(UserImage),
                typeof(System.Windows.Media.ImageSource),
                typeof(FrameControl),
                new PropertyMetadata(null));

        public System.Windows.Media.ImageSource UserImage
        {
            get => (System.Windows.Media.ImageSource)GetValue(UserImageProperty);
            set => SetValue(UserImageProperty, value);
        }

        // Optionally add any other missing properties as needed:
        // For example: Uninitialized, IsError, IsSignedInAndImageNull, etc.
    }
}
