(() => {
  document.addEventListener("click", event => {
    const openModal = event.target.closest("[data-appointment-modal-open]");
    if (openModal) {
      const modal = document.getElementById(openModal.getAttribute("data-appointment-modal-open"));
      if (modal) {
        modal.classList.add("open");
        modal.setAttribute("aria-hidden", "false");
      }
      return;
    }

    const closeModal = event.target.closest("[data-appointment-modal-close]");
    if (closeModal) {
      const modal = closeModal.closest(".appointment-modal");
      if (modal) {
        modal.classList.remove("open");
        modal.setAttribute("aria-hidden", "true");
      }
      return;
    }

    const target = event.target.closest(".appointment-placeholder");
    if (!target) {
      return;
    }

    event.preventDefault();
    window.alert("This action is a placeholder.");
  });
})();
