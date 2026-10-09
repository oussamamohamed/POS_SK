import Foundation

extension LocalPosAPI {
    public func staff() async throws -> [StaffMember] { try staffRepository.members() }

    public func createStaff(name: String, role: UserRole, pin: String) async throws {
        try requireManager()
        try validate(pin: pin)
        guard try !staffRepository.isPinInUse(pin) else { throw APIError.server(status: 400, message: "Ce code PIN est déjà attribué.") }
        try staffRepository.insert(StaffMember(name: name, role: role), pin: pin)
    }

    public func updateStaff(id: UUID, name: String, role: UserRole, pin: String?, isActive: Bool) async throws {
        try requireManager()
        if let pin {
            try validate(pin: pin)
            guard try !staffRepository.isPinInUse(pin, excluding: id) else { throw APIError.server(status: 400, message: "Ce code PIN est déjà attribué.") }
        }
        guard let current = try staffRepository.member(id: id) else { throw APIError.notFound(nil) }
        try ensureManagerRemains(after: current, becoming: (role, isActive))
        _ = try staffRepository.update(id: id, name: name, role: role, pin: pin, isActive: isActive)
    }

    public func deactivateStaff(id: UUID) async throws {
        try requireManager()
        guard let current = try staffRepository.member(id: id) else { throw APIError.notFound(nil) }
        try ensureManagerRemains(after: current, becoming: (current.role, false))
        _ = try staffRepository.update(id: id, name: current.name, role: current.role, pin: nil, isActive: false)
    }

    func validate(pin: String) throws {
        guard pin.count == Self.pinLength, pin.allSatisfy(\.isASCII), pin.allSatisfy(\.isNumber) else {
            throw APIError.server(status: 400, message: "Le code PIN doit comporter \(Self.pinLength) chiffres.")
        }
    }

    /// L'iPad n'a pas de serveur de secours : perdre le dernier responsable actif verrouillerait le back-office pour toujours.
    private func ensureManagerRemains(after member: StaffMember, becoming new: (role: UserRole, isActive: Bool)) throws {
        guard member.isActive, member.role.isManager, !(new.isActive && new.role.isManager) else { return }
        let activeManagers = try staffRepository.members().filter { $0.isActive && $0.role.isManager }
        if activeManagers.count <= 1 { throw APIError.server(status: 400, message: "Au moins un responsable actif est requis.") }
    }
}
