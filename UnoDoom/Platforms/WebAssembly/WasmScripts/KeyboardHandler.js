// Prevent default browser keyboard shortcuts that interfere with gameplay
(function () {
    // Helper function to prevent default behavior
    function preventDefaultAction(event) {
        event.preventDefault();
        return false;
    }

    // Helper function to check if the target element is editable
    function isEditableElement(element) {
        return element.tagName === 'INPUT' || 
               element.tagName === 'TEXTAREA' || 
               element.isContentEditable;
    }

    document.addEventListener('keydown', function (event) {
        // Note: event.key returns lowercase for letter keys when used with modifiers like Ctrl
        
        // Prevent Ctrl+W (close window) - W is used for forward movement, Ctrl for shooting
        if (event.ctrlKey && event.key === 'w') {
            return preventDefaultAction(event);
        }

        // Prevent Ctrl+T (new tab) - T key might be used in game
        if (event.ctrlKey && event.key === 't') {
            return preventDefaultAction(event);
        }

        // Prevent Ctrl+N (new window) - N key might be used in game
        if (event.ctrlKey && event.key === 'n') {
            return preventDefaultAction(event);
        }

        // Prevent F11 from toggling fullscreen (let the game handle it if needed)
        if (event.key === 'F11') {
            return preventDefaultAction(event);
        }

        // Prevent F5 and Ctrl+F5 (reload) during active gameplay
        if (event.key === 'F5') {
            return preventDefaultAction(event);
        }

        // Prevent Ctrl+R (reload) during active gameplay
        if (event.ctrlKey && event.key === 'r') {
            return preventDefaultAction(event);
        }

        // Prevent Ctrl+Q (close browser) - Q key might be used in game
        if (event.ctrlKey && event.key === 'q') {
            return preventDefaultAction(event);
        }

        // Prevent Backspace from navigating back (except in editable elements)
        if (event.key === 'Backspace' && !isEditableElement(event.target)) {
            return preventDefaultAction(event);
        }
    });
})();
