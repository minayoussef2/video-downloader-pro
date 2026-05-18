import customtkinter as ctk
import sys
import os

# Add the current directory to sys.path to allow absolute imports
current_dir = os.path.dirname(os.path.abspath(__file__))
if current_dir not in sys.path:
    sys.path.insert(0, current_dir)

from ui.main_window import MainWindow

def main():
    # Set appearance
    ctk.set_appearance_mode("Dark")
    ctk.set_default_color_theme("blue")

    # Initialize and run app
    app = MainWindow()
    app.mainloop()

if __name__ == "__main__":
    main()
