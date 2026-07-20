(() => {
  const updateUnavailableDateValues = picker => {
    const modal = picker.closest(".appointment-modal");
    const target = modal?.querySelector("[data-unavailable-date-values]");
    if (!target) {
      return;
    }

    target.value = Array.from(picker.querySelectorAll("[data-unavailable-date].selected"))
      .map(button => button.getAttribute("data-unavailable-date"))
      .join(",");
  };

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

    const unavailableDate = event.target.closest("[data-unavailable-date]");
    if (unavailableDate) {
      const picker = unavailableDate.closest("[data-unavailable-date-picker]");
      unavailableDate.classList.toggle("selected");
      unavailableDate.setAttribute("aria-pressed", unavailableDate.classList.contains("selected") ? "true" : "false");
      if (picker) {
        updateUnavailableDateValues(picker);
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
