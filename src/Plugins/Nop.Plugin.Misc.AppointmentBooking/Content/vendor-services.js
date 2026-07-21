(() => {
  const formatCalendarDate = date => {
    if (window.jQuery?.datepicker) {
      return window.jQuery.datepicker.formatDate("yy-mm-dd", date);
    }

    const year = date.getFullYear();
    const month = `${date.getMonth() + 1}`.padStart(2, "0");
    const day = `${date.getDate()}`.padStart(2, "0");
    return `${year}-${month}-${day}`;
  };

  const updateUnavailableDateValues = picker => {
    const modal = picker.closest(".appointment-modal");
    const target = modal?.querySelector("[data-unavailable-date-values]");
    if (!target) {
      return;
    }

    target.value = Array.from(picker.querySelectorAll("[data-unavailable-date-remove]"))
      .map(button => button.getAttribute("data-unavailable-date-remove"))
      .join(",");
  };

  const getSelectedUnavailableDates = picker => new Set(
    Array.from(picker.querySelectorAll("[data-unavailable-date-remove]"))
      .map(button => button.getAttribute("data-unavailable-date-remove"))
  );

  const refreshUnavailableCalendar = picker => {
    const calendar = picker?.querySelector("[data-unavailable-calendar]");
    if (!calendar || !window.jQuery?.fn?.datepicker || !window.jQuery.data(calendar, "datepicker")) {
      return;
    }

    window.jQuery(calendar).datepicker("refresh");
    updateUnavailableCalendarAria(picker);
  };

  const updateUnavailableCalendarAria = picker => {
    const calendar = picker?.querySelector("[data-unavailable-calendar]");
    if (!calendar) {
      return;
    }

    calendar.querySelectorAll(".ui-datepicker-calendar td").forEach(cell => {
      const link = cell.querySelector("a");
      if (!link) {
        return;
      }

      link.setAttribute("aria-pressed", cell.classList.contains("appointment-datepicker-selected") ? "true" : "false");
    });
  };

  const removeUnavailableDate = (picker, value) => {
    const selectedDates = picker?.querySelector("[data-unavailable-selected-dates]");
    if (!selectedDates || !value) {
      return;
    }

    selectedDates.querySelector(`[data-unavailable-date-remove="${value}"]`)?.closest(".appointment-selected-date")?.remove();
    updateUnavailableDateValues(picker);
    refreshUnavailableCalendar(picker);
  };

  const renderUnavailableDate = (picker, value) => {
    const selectedDates = picker.querySelector("[data-unavailable-selected-dates]");
    if (!selectedDates || !value) {
      return;
    }

    if (selectedDates.querySelector(`[data-unavailable-date-remove="${value}"]`)) {
      return;
    }

    const pill = document.createElement("span");
    pill.className = "appointment-selected-date";
    pill.textContent = value;

    const remove = document.createElement("button");
    remove.type = "button";
    remove.setAttribute("data-unavailable-date-remove", value);
    remove.setAttribute("aria-label", `Remove ${value}`);
    remove.textContent = "x";

    pill.appendChild(remove);
    selectedDates.appendChild(pill);
    updateUnavailableDateValues(picker);
    refreshUnavailableCalendar(picker);
  };

  const initializeUnavailableCalendars = root => {
    if (!window.jQuery?.fn?.datepicker) {
      return;
    }

    root.querySelectorAll("[data-unavailable-calendar]").forEach(calendar => {
      const picker = calendar.closest("[data-unavailable-date-picker]");
      if (window.jQuery.data(calendar, "datepicker")) {
        window.jQuery(calendar).datepicker("refresh");
        updateUnavailableCalendarAria(picker);
        return;
      }

      window.jQuery(calendar).datepicker({
        dateFormat: "yy-mm-dd",
        changeMonth: true,
        changeYear: true,
        beforeShowDay: date => {
          const selected = picker && getSelectedUnavailableDates(picker).has(formatCalendarDate(date));
          return [true, selected ? "appointment-datepicker-selected" : ""];
        },
        onSelect: dateText => {
          if (picker) {
            if (getSelectedUnavailableDates(picker).has(dateText)) {
              removeUnavailableDate(picker, dateText);
            } else {
              renderUnavailableDate(picker, dateText);
            }
          }
        }
      });

      updateUnavailableCalendarAria(picker);
    });
  };

  const fallbackCopyText = text => {
    const textArea = document.createElement("textarea");
    textArea.value = text;
    textArea.setAttribute("readonly", "");
    textArea.style.position = "fixed";
    textArea.style.left = "-9999px";
    document.body.appendChild(textArea);
    textArea.select();

    try {
      document.execCommand("copy");
    } finally {
      textArea.remove();
    }
  };

  const copyText = text => {
    if (navigator.clipboard?.writeText) {
      return navigator.clipboard.writeText(text);
    }

    fallbackCopyText(text);
    return Promise.resolve();
  };

  document.addEventListener("click", event => {
    const bookingTab = event.target.closest("[data-appointment-booking-tab]");
    if (bookingTab) {
      const tabs = bookingTab.closest(".appointment-booking-tabs");
      if (tabs) {
        tabs.querySelectorAll("[data-appointment-booking-tab]").forEach(tab => {
          tab.classList.toggle("active", tab === bookingTab);
        });
      }
      return;
    }

    const openModal = event.target.closest("[data-appointment-modal-open]");
    if (openModal) {
      const modal = document.getElementById(openModal.getAttribute("data-appointment-modal-open"));
      if (modal) {
        modal.classList.add("open");
        modal.setAttribute("aria-hidden", "false");
        initializeUnavailableCalendars(modal);
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

    const removeUnavailableDateButton = event.target.closest("[data-unavailable-date-remove]");
    if (removeUnavailableDateButton) {
      const picker = removeUnavailableDateButton.closest("[data-unavailable-date-picker]");
      if (picker) {
        removeUnavailableDate(picker, removeUnavailableDateButton.getAttribute("data-unavailable-date-remove"));
      }
      return;
    }

    const copyButton = event.target.closest("[data-service-copy-url]");
    if (copyButton) {
      event.preventDefault();

      const url = copyButton.getAttribute("data-service-copy-url");
      if (!url) {
        return;
      }

      const absoluteUrl = new URL(url, window.location.origin).toString();
      copyText(absoluteUrl).then(() => {
        copyButton.classList.add("copied");
        copyButton.setAttribute("aria-label", "Copied");
        copyButton.setAttribute("title", "Copied");
        copyButton.setAttribute("data-tooltip", "Copied");
      });
      return;
    }

    const target = event.target.closest(".appointment-placeholder");
    if (!target) {
      return;
    }

    event.preventDefault();
    window.alert("This action is a placeholder.");
  });

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", () => initializeUnavailableCalendars(document));
  } else {
    initializeUnavailableCalendars(document);
  }
})();
