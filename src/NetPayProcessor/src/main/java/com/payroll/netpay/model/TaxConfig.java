package com.payroll.netpay.model;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.fasterxml.jackson.annotation.JsonProperty;

@JsonIgnoreProperties(ignoreUnknown = true)
public class TaxConfig {
    @JsonProperty("employeeId")
    private String employeeId;

    @JsonProperty("federalFilingStatus")
    private String federalFilingStatus;

    @JsonProperty("state")
    private String state;

    @JsonProperty("additionalFederalWithholding")
    private double additionalFederalWithholding;

    @JsonProperty("additionalStateWithholding")
    private double additionalStateWithholding;

    public TaxConfig() {}

    public TaxConfig(String employeeId, String federalFilingStatus, String state,
                     double additionalFederalWithholding, double additionalStateWithholding) {
        this.employeeId = employeeId;
        this.federalFilingStatus = federalFilingStatus;
        this.state = state;
        this.additionalFederalWithholding = additionalFederalWithholding;
        this.additionalStateWithholding = additionalStateWithholding;
    }

    public String getEmployeeId() { return employeeId; }
    public void setEmployeeId(String employeeId) { this.employeeId = employeeId; }

    public String getFederalFilingStatus() { return federalFilingStatus; }
    public void setFederalFilingStatus(String federalFilingStatus) { this.federalFilingStatus = federalFilingStatus; }

    public String getState() { return state; }
    public void setState(String state) { this.state = state; }

    public double getAdditionalFederalWithholding() { return additionalFederalWithholding; }
    public void setAdditionalFederalWithholding(double additionalFederalWithholding) { this.additionalFederalWithholding = additionalFederalWithholding; }

    public double getAdditionalStateWithholding() { return additionalStateWithholding; }
    public void setAdditionalStateWithholding(double additionalStateWithholding) { this.additionalStateWithholding = additionalStateWithholding; }
}
