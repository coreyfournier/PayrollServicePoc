package com.payroll.netpay.model;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.fasterxml.jackson.annotation.JsonProperty;
import java.util.HashMap;
import java.util.Map;

@JsonIgnoreProperties(ignoreUnknown = true)
public class DeductionMap {
    @JsonProperty("employeeId")
    private String employeeId;

    @JsonProperty("deductions")
    private Map<String, DeductionEntry> deductions = new HashMap<>();

    public DeductionMap() {}

    public DeductionMap(String employeeId) {
        this.employeeId = employeeId;
    }

    public String getEmployeeId() { return employeeId; }
    public void setEmployeeId(String employeeId) { this.employeeId = employeeId; }

    public Map<String, DeductionEntry> getDeductions() { return deductions; }
    public void setDeductions(Map<String, DeductionEntry> deductions) { this.deductions = deductions; }

    public void putDeduction(String deductionId, double amount, boolean isPercentage, boolean isActive) {
        deductions.put(deductionId, new DeductionEntry(amount, isPercentage, isActive));
    }

    public double computeFixedTotal() {
        return deductions.values().stream()
                .filter(d -> d.isActive() && !d.isPercentage())
                .mapToDouble(DeductionEntry::getAmount)
                .sum();
    }

    public double computePercentTotal(double grossPay) {
        return deductions.values().stream()
                .filter(d -> d.isActive() && d.isPercentage())
                .mapToDouble(d -> (d.getAmount() / 100.0) * grossPay)
                .sum();
    }

    @JsonIgnoreProperties(ignoreUnknown = true)
    public static class DeductionEntry {
        @JsonProperty("amount")
        private double amount;

        @JsonProperty("isPercentage")
        private boolean isPercentage;

        @JsonProperty("isActive")
        private boolean isActive;

        public DeductionEntry() {}

        public DeductionEntry(double amount, boolean isPercentage, boolean isActive) {
            this.amount = amount;
            this.isPercentage = isPercentage;
            this.isActive = isActive;
        }

        public double getAmount() { return amount; }
        public void setAmount(double amount) { this.amount = amount; }

        public boolean isPercentage() { return isPercentage; }
        public void setPercentage(boolean percentage) { isPercentage = percentage; }

        public boolean isActive() { return isActive; }
        public void setActive(boolean active) { isActive = active; }
    }
}
