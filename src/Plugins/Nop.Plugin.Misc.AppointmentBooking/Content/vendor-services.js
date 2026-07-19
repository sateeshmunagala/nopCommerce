(() => {
  document.addEventListener("click", event => {
    const target = event.target.closest(".appointment-placeholder");
    if (!target) {
      return;
    }

    event.preventDefault();
    window.alert("This action is a placeholder.");
  });
})();
