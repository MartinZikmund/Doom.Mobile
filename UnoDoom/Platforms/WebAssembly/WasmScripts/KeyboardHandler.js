// Prevent default browser keyboard shortcuts that interfere with gameplay
(function () {
    document.addEventListener('keydown', function (event) {
        // Prevent Ctrl+W (close window) - W is used for forward movement, Ctrl for shooting
        if (event.ctrlKey && event.key === 'w') {
            event.preventDefault();
            return false;
        }

        // Prevent Ctrl+T (new tab) - T key might be used in game
        if (event.ctrlKey && event.key === 't') {
            event.preventDefault();
            return false;
        }

        // Prevent Ctrl+N (new window) - N key might be used in game
        if (event.ctrlKey && event.key === 'n') {
            event.preventDefault();
            return false;
        }

        // Prevent F11 from toggling fullscreen (let the game handle it if needed)
        if (event.key === 'F11') {
            event.preventDefault();
            return false;
        }

        // Prevent Ctrl+R and Ctrl+F5 (reload) during active gameplay
        if ((event.ctrlKey && event.key === 'r') || (event.ctrlKey && event.key === 'F5')) {
            event.preventDefault();
            return false;
        }

        // Prevent Ctrl+Q (close browser) - Q key might be used in game
        if (event.ctrlKey && event.key === 'q') {
            event.preventDefault();
            return false;
        }

        // Prevent Backspace from navigating back
        if (event.key === 'Backspace' && event.target.tagName !== 'INPUT' && event.target.tagName !== 'TEXTAREA') {
            event.preventDefault();
            return false;
        }
    });
})();
