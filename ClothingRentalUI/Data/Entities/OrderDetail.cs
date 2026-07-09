namespace ClothingRentalUI.Data.Entities;

public class OrderDetail
{
    public int Id { get; set; }
    
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    
    // Lưu thông số tại thời điểm thuê phòng ngừa bảng giá thay đổi sau này
    public decimal RentPrice { get; set; }
    public decimal Deposit { get; set; }
    
    public int RentDays { get; set; } = 1; // Số ngày thuê mặc định ban đầu
    
    // Thông tin phát sinh lúc trả hàng / gia hạn
    public int ExtendedDays { get; set; } = 0;
    public decimal PenaltyFee { get; set; } = 0;
    public string? PenaltyReason { get; set; }
    
    public bool IsReturned { get; set; } = false; // Trạng thái đã trả đồ thực tế
}
