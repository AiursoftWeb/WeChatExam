document.addEventListener("DOMContentLoaded", function () {
        const data = document.getElementById("dashboard-data").dataset;
        function getLocalizedText(key, defaultText) {
            const el = document.querySelector(`#loc-data span[data-key="${key}"]`);
            return el ? el.innerText : defaultText;
        }

        // --- Question Type Doughnut ---
        var ctxQ = document.getElementById("chartQuestionTypes");
        if (ctxQ) {
            new Chart(ctxQ, {
                type: "doughnut",
                data: {
                    labels: [
                        getLocalizedText("choice", "Choice"),
                        getLocalizedText("blank", "Blank"),
                        getLocalizedText("bool", "Bool"),
                        getLocalizedText("short-answer", "Short Answer"),
                        getLocalizedText("essay", "Essay"),
                        getLocalizedText("noun-explanation", "Noun Explanation")
                    ],
                    datasets: [{
                        data: JSON.parse(data.questionTypes),
                        backgroundColor: [
                            "rgba(59, 125, 221, 0.85)",
                            "rgba(40, 167, 69, 0.85)",
                            "rgba(255, 193, 7, 0.85)",
                            "rgba(23, 162, 184, 0.85)",
                            "rgba(220, 53, 69, 0.85)",
                            "rgba(108, 117, 125, 0.85)"
                        ],
                        borderWidth: 3,
                        borderColor: "#fff"
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    cutout: "65%",
                    plugins: {
                        legend: {
                            position: "bottom",
                            labels: { padding: 16, usePointStyle: true, pointStyle: "circle" }
                        }
                    }
                }
            });
        }

        // --- Paper Status Pie ---
        var ctxP = document.getElementById("chartPaperStatus");
        if (ctxP) {
            new Chart(ctxP, {
                type: "pie",
                data: {
                    labels: [
                        getLocalizedText("unpublished", "Unpublished"),
                        getLocalizedText("published", "Published")
                    ],
                    datasets: [{
                        data: JSON.parse(data.paperStatus),
                        backgroundColor: [
                            "rgba(255, 193, 7, 0.85)",
                            "rgba(40, 167, 69, 0.85)"
                        ],
                        borderWidth: 3,
                        borderColor: "#fff"
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: {
                            position: "bottom",
                            labels: { padding: 16, usePointStyle: true, pointStyle: "circle" }
                        }
                    }
                }
            });
        }

        // --- Top Tags Bar ---
        var ctxTags = document.getElementById("chartTopTags");
        if (ctxTags) {
            new Chart(ctxTags, {
                type: "bar",
                data: {
                    labels: JSON.parse(data.topTags).map(t => t.Name),
                    datasets: [{
                        label: getLocalizedText("usage-count", "Usage Count"),
                        data: JSON.parse(data.topTags).map(t => t.UsageCount),
                        backgroundColor: "rgba(59, 125, 221, 0.85)",
                        borderColor: "rgba(59, 125, 221, 0.85)",
                        borderWidth: 1,
                        borderRadius: 4,
                        barPercentage: 0.5,
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    indexAxis: 'y', // Horizontal bar chart
                    plugins: {
                        legend: { display: false }
                    },
                    scales: {
                        x: {
                            beginAtZero: true,
                            ticks: { stepSize: 1 }
                        }
                    }
                }
            });
        }

        // --- Taxonomy Stats Pie ---
        var ctxTax = document.getElementById("chartTaxonomyStats");
        if (ctxTax) {
            new Chart(ctxTax, {
                type: "pie",
                data: {
                    labels: JSON.parse(data.taxonomyStats).map(t => t.Name),
                    datasets: [{
                        data: JSON.parse(data.taxonomyStats).map(t => t.TagCount),
                        backgroundColor: [
                            "rgba(59, 125, 221, 0.85)",
                            "rgba(255, 193, 7, 0.85)",
                            "rgba(40, 167, 69, 0.85)",
                            "rgba(220, 53, 69, 0.85)",
                            "rgba(23, 162, 184, 0.85)"
                        ],
                        borderWidth: 2,
                        borderColor: "#fff"
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: {
                            position: "bottom",
                            labels: { padding: 16, usePointStyle: true, pointStyle: "circle" }
                        }
                    }
                }
            });
        }

        // --- Exam Status Bar ---
        var ctxE = document.getElementById("chartExamStatus");
        if (ctxE) {
            new Chart(ctxE, {
                type: "bar",
                data: {
                    labels: [
                        getLocalizedText("active", "Active"),
                        getLocalizedText("upcoming", "Upcoming"),
                        getLocalizedText("ended", "Ended")
                    ],
                    datasets: [{
                        label: getLocalizedText("exams", "Exams"),
                        data: JSON.parse(data.examStatus),
                        backgroundColor: [
                            "rgba(40, 167, 69, 0.85)",
                            "rgba(23, 162, 184, 0.85)",
                            "rgba(108, 117, 125, 0.85)"
                        ],
                        borderRadius: 6,
                        barPercentage: 0.6,
                        categoryPercentage: 0.6
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: { display: false }
                    },
                    scales: {
                        y: {
                            beginAtZero: true,
                            ticks: { stepSize: 1 }
                        }
                    }
                }
            });
        }
    });
