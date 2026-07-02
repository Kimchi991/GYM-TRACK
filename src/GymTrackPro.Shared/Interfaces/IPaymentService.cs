using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GymTrackPro.Shared.DTOs;

namespace GymTrackPro.Shared.Interfaces;

public interface IPaymentService
{
    Task<PaymentResponseDto?> GetByIdAsync(int id);
    Task<IEnumerable<PaymentResponseDto>> GetByMemberIdAsync(int memberId);
    Task<PaymentResponseDto> ProcessPaymentAsync(CreatePaymentDto paymentDto);
    Task<PaymentResponseDto> RefundPaymentAsync(int id);
    Task<IEnumerable<PaymentResponseDto>> SearchPaymentsAsync(
        DateTime? date,
        string? method,
        string? status,
        int? memberId,
        string? receiptNumber);
}
