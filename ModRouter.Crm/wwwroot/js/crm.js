// Browser-frame helpers: the frame's Back, Forward and Reload buttons drive the real history.
// The confirm dialog is a native <dialog>; showModal makes it modal, traps focus and honours Escape.
window.crmDialog = {
    open: function (el) { if (el && !el.open) { el.showModal(); } }
};

window.crmFrame = {
    back: function () { history.back(); },
    forward: function () { history.forward(); },
    reload: function () { location.reload(); },
    copy: function (text) { return navigator.clipboard ? navigator.clipboard.writeText(text) : Promise.resolve(); }
};
