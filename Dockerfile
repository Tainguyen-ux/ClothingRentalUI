# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Sao chép file project và restore các dependencies trước để tận dụng Docker cache
COPY ["ClothingRentalUI/ClothingRentalUI.csproj", "ClothingRentalUI/"]
RUN dotnet restore "ClothingRentalUI/ClothingRentalUI.csproj"

# Sao chép toàn bộ mã nguồn và build dự án ở chế độ Release
COPY . .
WORKDIR "/src/ClothingRentalUI"
RUN dotnet build "ClothingRentalUI.csproj" -c Release -o /app/build

# Stage 2: Publish ứng dụng
FROM build AS publish
RUN dotnet publish "ClothingRentalUI.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Cấu hình biến môi trường của ASP.NET Core
ENV ASPNETCORE_ENVIRONMENT=Production

# Khởi chạy ứng dụng và lắng nghe trên cổng động ($PORT) do Railway tự động cung cấp
# Nếu chạy cục bộ không có $PORT, hệ thống sẽ mặc định nghe ở cổng 8080
CMD ["sh", "-c", "dotnet ClothingRentalUI.dll --urls http://0.0.0.0:${PORT:-8080}"]
