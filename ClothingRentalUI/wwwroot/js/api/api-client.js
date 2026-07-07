/**
 * API Client phục vụ việc gọi các API trực tiếp từ Client-side (Trình duyệt).
 * Hỗ trợ tự động đính kèm Token đăng nhập và xử lý định dạng phản hồi chuẩn.
 */
class ApiClient {
    constructor(baseUrl = '') {
        // Có thể cấu hình địa chỉ API ngoài hoặc proxy tại chỗ
        this.baseUrl = baseUrl || window.location.origin + '/';
    }

    /**
     * Lấy token JWT từ lưu trữ ở Client-side (localStorage/sessionStorage)
     */
    getToken() {
        return localStorage.getItem('api_token') || sessionStorage.getItem('api_token') || '';
    }

    /**
     * Lưu token khi đăng nhập thành công từ client-side
     */
    setToken(token, rememberMe = false) {
        if (rememberMe) {
            localStorage.setItem('api_token', token);
        } else {
            sessionStorage.setItem('api_token', token);
        }
    }

    /**
     * Xóa token (khi đăng xuất hoặc hết hạn)
     */
    clearToken() {
        localStorage.removeItem('api_token');
        sessionStorage.removeItem('api_token');
    }

    /**
     * Phương thức lõi thực thi yêu cầu HTTP qua fetch()
     */
    async request(endpoint, options = {}) {
        const url = endpoint.startsWith('http') ? endpoint : `${this.baseUrl}${endpoint.replace(/^\//, '')}`;
        
        const headers = {
            'Content-Type': 'application/json',
            ...options.headers,
        };

        // Tự động thêm Bearer Token nếu tồn tại
        const token = this.getToken();
        if (token) {
            headers['Authorization'] = `Bearer ${token}`;
        }

        const config = {
            ...options,
            headers,
        };

        try {
            const response = await fetch(url, config);
            
            // Xử lý trường hợp Unauthorized
            if (response.status === 401) {
                this.clearToken();
                console.warn('Phiên đăng nhập hết hạn hoặc không có quyền.');
                // Có thể bắn ra một event hoặc chuyển hướng người dùng về trang đăng nhập
                window.dispatchEvent(new CustomEvent('unauthorized-api-call'));
            }

            const data = await response.json();
            return data; // Trả về cấu trúc ApiResponse { success, message, data }
        } catch (error) {
            console.error(`Lỗi gọi API (${url}):`, error);
            return {
                success: false,
                message: 'Không thể kết nối đến máy chủ API.'
            };
        }
    }

    get(endpoint, options = {}) {
        return this.request(endpoint, { ...options, method: 'GET' });
    }

    post(endpoint, body, options = {}) {
        return this.request(endpoint, { ...options, method: 'POST', body: JSON.stringify(body) });
    }

    put(endpoint, body, options = {}) {
        return this.request(endpoint, { ...options, method: 'PUT', body: JSON.stringify(body) });
    }

    delete(endpoint, options = {}) {
        return this.request(endpoint, { ...options, method: 'DELETE' });
    }
}

// Cấu hình URL cơ sở cho Client-side API gọi trực tiếp
const apiClient = new ApiClient('https://api.clothingrental.example.com/api/');

export default apiClient;
