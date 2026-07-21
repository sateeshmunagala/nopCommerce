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

  const updateScheduleIntervalNames = row => {
    const dayIndex = row.getAttribute("data-schedule-day-index");
    row.querySelectorAll("[data-schedule-interval]").forEach((interval, intervalIndex) => {
      const start = interval.querySelector("[data-schedule-start]");
      const end = interval.querySelector("[data-schedule-end]");
      if (start) {
        start.name = `Schedule[${dayIndex}].Intervals[${intervalIndex}].StartTime`;
        start.id = `Schedule_${dayIndex}__Intervals_${intervalIndex}__StartTime`;
      }

      if (end) {
        end.name = `Schedule[${dayIndex}].Intervals[${intervalIndex}].EndTime`;
        end.id = `Schedule_${dayIndex}__Intervals_${intervalIndex}__EndTime`;
      }

      const remove = interval.querySelector("[data-schedule-remove-interval]");
      if (remove) {
        remove.hidden = intervalIndex === 0;
      }
    });
  };

  const updateScheduleApplyButtons = root => {
    const scheduleRoot = root.closest(".appointment-schedule-list") || document;
    scheduleRoot.querySelectorAll("[data-schedule-apply-all]").forEach(button => {
      button.hidden = true;
    });

    const firstEnabledRow = Array.from(scheduleRoot.querySelectorAll("[data-schedule-day-row]"))
      .find(row => row.querySelector("[data-schedule-day-toggle]")?.checked);
    firstEnabledRow?.querySelector("[data-schedule-apply-all]")?.removeAttribute("hidden");
  };

  const setScheduleRowState = row => {
    const enabled = row.querySelector("[data-schedule-day-toggle]")?.checked;
    row.classList.toggle("enabled", enabled);
    row.classList.toggle("disabled", !enabled);
    row.querySelectorAll("[data-schedule-start], [data-schedule-end]").forEach(select => {
      select.disabled = !enabled;
    });
    row.querySelectorAll("[data-schedule-add-interval], [data-schedule-remove-interval], [data-schedule-apply-all]").forEach(button => {
      button.disabled = !enabled;
    });
    updateScheduleIntervalNames(row);
    updateScheduleApplyButtons(row);
  };

  const initializeScheduleRows = root => {
    root.querySelectorAll("[data-schedule-day-row]").forEach(row => {
      setScheduleRowState(row);
    });
  };

  const addScheduleInterval = row => {
    const intervals = row.querySelector("[data-schedule-intervals]");
    const firstInterval = intervals?.querySelector("[data-schedule-interval]");
    if (!intervals || !firstInterval) {
      return;
    }

    const nextInterval = firstInterval.cloneNode(true);
    nextInterval.querySelector("[data-schedule-apply-all]")?.setAttribute("hidden", "");
    nextInterval.querySelector("[data-schedule-remove-interval]")?.removeAttribute("hidden");
    nextInterval.querySelectorAll("select").forEach(select => {
      select.disabled = false;
    });
    intervals.appendChild(nextInterval);
    updateScheduleIntervalNames(row);
  };

  const removeScheduleInterval = button => {
    const row = button.closest("[data-schedule-day-row]");
    const intervals = row?.querySelectorAll("[data-schedule-interval]");
    if (!row || !intervals || intervals.length <= 1) {
      return;
    }

    button.closest("[data-schedule-interval]")?.remove();
    updateScheduleIntervalNames(row);
    updateScheduleApplyButtons(row);
  };

  const applyScheduleIntervalToAll = button => {
    const sourceInterval = button.closest("[data-schedule-interval]");
    const sourceRow = button.closest("[data-schedule-day-row]");
    const sourceStart = sourceInterval?.querySelector("[data-schedule-start]")?.value;
    const sourceEnd = sourceInterval?.querySelector("[data-schedule-end]")?.value;
    const scheduleRoot = sourceRow?.closest(".appointment-schedule-list");
    if (!sourceStart || !sourceEnd || !scheduleRoot) {
      return;
    }

    scheduleRoot.querySelectorAll("[data-schedule-day-row]").forEach(row => {
      if (!row.querySelector("[data-schedule-day-toggle]")?.checked) {
        return;
      }

      const firstInterval = row.querySelector("[data-schedule-interval]");
      const start = firstInterval?.querySelector("[data-schedule-start]");
      const end = firstInterval?.querySelector("[data-schedule-end]");
      if (start) {
        start.value = sourceStart;
      }

      if (end) {
        end.value = sourceEnd;
      }
    });
  };

  const getScheduleTime = value => {
    if (!value || !value.includes(":")) {
      return null;
    }

    const parts = value.split(":").map(part => Number.parseInt(part, 10));
    return Number.isNaN(parts[0]) || Number.isNaN(parts[1]) ? null : (parts[0] * 60) + parts[1];
  };

  const validateScheduleForm = form => {
    const validation = form.querySelector("[data-schedule-client-validation]");
    const errors = [];
    const enabledRows = Array.from(form.querySelectorAll("[data-schedule-day-row]"))
      .filter(row => row.querySelector("[data-schedule-day-toggle]")?.checked);

    if (!enabledRows.length) {
      errors.push("Select at least one available day.");
    }

    enabledRows.forEach(row => {
      const dayName = row.querySelector(".appointment-schedule-day span")?.textContent || "day";
      const intervals = Array.from(row.querySelectorAll("[data-schedule-interval]"));
      if (!intervals.length) {
        errors.push(`Add at least one time slot for ${dayName}.`);
        return;
      }

      const parsedIntervals = [];
      intervals.forEach(interval => {
        const start = getScheduleTime(interval.querySelector("[data-schedule-start]")?.value);
        const end = getScheduleTime(interval.querySelector("[data-schedule-end]")?.value);
        if (start === null) {
          errors.push(`Select a start time for ${dayName}.`);
        }

        if (end === null) {
          errors.push(`Select an end time for ${dayName}.`);
        }

        if (start === null || end === null) {
          return;
        }

        if (start >= end) {
          errors.push(`Start time must be earlier than end time for ${dayName}.`);
          return;
        }

        parsedIntervals.push({ start, end });
      });

      parsedIntervals.sort((first, second) => first.start - second.start);
      for (let i = 1; i < parsedIntervals.length; i += 1) {
        if (parsedIntervals[i].start < parsedIntervals[i - 1].end) {
          errors.push(`Time slots cannot overlap for ${dayName}.`);
          break;
        }
      }
    });

    if (validation) {
      validation.hidden = !errors.length;
      validation.innerHTML = errors.length ? errors.map(error => `<div>${error}</div>`).join("") : "";
    }

    return !errors.length;
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

    const addIntervalButton = event.target.closest("[data-schedule-add-interval]");
    if (addIntervalButton) {
      addScheduleInterval(addIntervalButton.closest("[data-schedule-day-row]"));
      return;
    }

    const removeIntervalButton = event.target.closest("[data-schedule-remove-interval]");
    if (removeIntervalButton) {
      removeScheduleInterval(removeIntervalButton);
      return;
    }

    const applyAllButton = event.target.closest("[data-schedule-apply-all]");
    if (applyAllButton) {
      applyScheduleIntervalToAll(applyAllButton);
      return;
    }

    const target = event.target.closest(".appointment-placeholder");
    if (!target) {
      return;
    }

    event.preventDefault();
    window.alert("This action is a placeholder.");
  });

  document.addEventListener("change", event => {
    const scheduleToggle = event.target.closest("[data-schedule-day-toggle]");
    if (scheduleToggle) {
      setScheduleRowState(scheduleToggle.closest("[data-schedule-day-row]"));
    }
  });

  document.addEventListener("submit", event => {
    if (!event.target.querySelector("[data-schedule-client-validation]")) {
      return;
    }

    event.target.querySelectorAll("[data-schedule-day-row]").forEach(row => {
      setScheduleRowState(row);
    });

    if (!validateScheduleForm(event.target)) {
      event.preventDefault();
    }
  });

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", () => {
      initializeUnavailableCalendars(document);
      initializeScheduleRows(document);
    });
  } else {
    initializeUnavailableCalendars(document);
    initializeScheduleRows(document);
  }
})();
