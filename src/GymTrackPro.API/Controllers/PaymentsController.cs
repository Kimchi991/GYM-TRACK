using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Controllers;

/// <summary>
/// Provides endpoints for processing, searching, and refunding member payments.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    public PaymentsController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    /// <summary>
    /// Retrieves a specific payment record by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the payment.</param>
    /// <returns>A standardized API response containing the payment details.</returns>
    /// <response code="200">If the payment record is found.</response>
    /// <response code="404">If the payment record does not exist.</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<PaymentResponseDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse), 404)]
    public async Task<IActionResult> GetById(int id)
    {
        var payment = await _paymentService.GetByIdAsync(id);
        if (payment == null)
        {
            return NotFound(ApiResponse.FailureResponse("Payment record not found."));
        }
        return Ok(ApiResponse<PaymentResponseDto>.SuccessResponse(payment));
    }

    /// <summary>
    /// Retrieves all payment records linked to a specific member.
    /// </summary>
    /// <param name="memberId">The unique identifier of the member.</param>
    /// <returns>A standardized API response listing the member's transactions.</returns>
    [HttpGet("member/{memberId:int}")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<PaymentResponseDto>>), 200)]
    public async Task<IActionResult> GetByMemberId(int memberId)
    {
        var payments = await _paymentService.GetByMemberIdAsync(memberId);
        return Ok(ApiResponse<IEnumerable<PaymentResponseDto>>.SuccessResponse(payments));
    }

    /// <summary>
    /// Processes a new payment for a subscription.
    /// </summary>
    /// <param name="paymentDto">The transaction parameters.</param>
    /// <returns>A standardized API response containing the processed payment receipt details.</returns>
    /// <response code="201">If the payment is processed successfully.</response>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<PaymentResponseDto>), 201)]
    public async Task<IActionResult> Create([FromBody] CreatePaymentDto paymentDto)
    {
        var payment = await _paymentService.ProcessPaymentAsync(paymentDto);
        return CreatedAtAction(nameof(GetById), new { id = payment.PaymentID }, ApiResponse<PaymentResponseDto>.SuccessResponse(payment, "Payment processed successfully."));
    }

    /// <summary>
    /// Refunds an existing completed payment transaction. Restricted to Administrators.
    /// </summary>
    /// <param name="id">The unique identifier of the payment to refund.</param>
    /// <returns>A standardized API response confirming the refund.</returns>
    /// <response code="200">If the refund was processed successfully.</response>
    [HttpPost("{id:int}/refund")]
    [Authorize(Roles = "Administrator")]
    [ProducesResponseType(typeof(ApiResponse<PaymentResponseDto>), 200)]
    public async Task<IActionResult> Refund(int id)
    {
        var payment = await _paymentService.RefundPaymentAsync(id);
        return Ok(ApiResponse<PaymentResponseDto>.SuccessResponse(payment, "Payment refunded successfully."));
    }

    /// <summary>
    /// Searches and filters gym payment transactions.
    /// </summary>
    /// <param name="date">Filter by transaction date.</param>
    /// <param name="method">Filter by payment method (e.g. Cash, GCash, Maya, Card, BankTransfer).</param>
    /// <param name="status">Filter by payment status (e.g. Pending, Paid, Failed, Cancelled, Refunded).</param>
    /// <param name="memberId">Filter by specific member identifier.</param>
    /// <param name="receiptNumber">Filter by partial/exact receipt number matching.</param>
    /// <returns>A list of matching payment records.</returns>
    [HttpGet("search")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<PaymentResponseDto>>), 200)]
    public async Task<IActionResult> Search(
        [FromQuery] DateTime? date,
        [FromQuery] string? method,
        [FromQuery] string? status,
        [FromQuery] int? memberId,
        [FromQuery] string? receiptNumber)
    {
        var payments = await _paymentService.SearchPaymentsAsync(date, method, status, memberId, receiptNumber);
        return Ok(ApiResponse<IEnumerable<PaymentResponseDto>>.SuccessResponse(payments, "Payments retrieved successfully."));
    }
}
