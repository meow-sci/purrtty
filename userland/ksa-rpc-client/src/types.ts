/**
 * RPC request structure sent to the game server
 */
export interface RpcRequest {
  /** Action name to invoke */
  action: string;
  /** Optional parameters for the action */
  params?: Record<string, unknown>;
}

/**
 * RPC response structure received from the game server
 */
export interface RpcResponse<T = unknown> {
  /** Whether the request succeeded */
  success: boolean;
  /** Response data on success */
  data?: T;
  /** Error message on failure */
  error?: string;
}

/**
 * Information about a craft in the game
 */
export interface CraftInfo {
  /** Unique craft identifier */
  id: number;
  /** Craft name */
  name: string;
  /** Whether the craft is currently controlled by the player */
  isControlled?: boolean;
}

/**
 * Configuration options for the RPC client
 */
export interface ClientOptions {
  /** Request timeout in milliseconds (default: 5000) */
  timeout?: number;
}
